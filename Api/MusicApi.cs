using System.Text;
using Newtonsoft.Json.Linq;

namespace CloudMusicBot.Api;

/// <summary>Raised for anything the api could not answer; the message is safe to show to users.</summary>
public sealed class BotApiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Everything the bot needs from the NetEase Cloud Music api, with the JSON
/// parsing and null handling kept in one place.
/// </summary>
public sealed class MusicApi : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly AppConfig _config;
    private readonly UserSessionStore _sessions;

    public MusicApi(AppConfig config, UserSessionStore sessions)
    {
        _config = config;
        _sessions = sessions;
    }

    public async Task<IReadOnlyList<Music>> SearchSongsAsync(string keyword, ulong userId, CancellationToken ct = default)
    {
        var json = await GetAsync("/search", [("keywords", keyword), ("type", "1"), ("limit", "20")], userId, ct);
        return ReadSongs(json["result"]?["songs"] as JArray);
    }

    public async Task<IReadOnlyList<UserSummary>> SearchUsersAsync(string keyword, ulong userId, CancellationToken ct = default)
    {
        var json = await GetAsync("/search", [("keywords", keyword), ("type", "1002"), ("limit", "20")], userId, ct);
        if (json["result"]?["userprofiles"] is not JArray users)
        {
            return [];
        }

        return users
            .Select(user => new UserSummary((ulong?)user["userId"] ?? 0, (string?)user["nickname"] ?? ""))
            .Where(user => user.Id != 0)
            .ToArray();
    }

    /// <summary>Song details for the queue; <paramref name="owner"/> becomes the requester.</summary>
    public async Task<Music?> GetSongAsync(ulong songId, ulong owner, CancellationToken ct = default)
    {
        var songs = await GetSongsAsync([songId], owner, ct);
        return songs.Count == 0 ? null : songs[0];
    }

    public async Task<IReadOnlyList<Music>> GetSongsAsync(IEnumerable<ulong> songIds, ulong owner, CancellationToken ct = default)
    {
        var ids = string.Join(",", songIds);
        if (ids.Length == 0)
        {
            return [];
        }

        var json = await GetAsync("/song/detail", [("ids", ids)], owner, ct);
        return ReadSongs(json["songs"] as JArray, owner);
    }

    public async Task<IReadOnlyList<PlaylistSummary>> GetUserPlaylistsAsync(ulong neteaseUserId, ulong userId, CancellationToken ct = default)
    {
        var json = await GetAsync("/user/playlist", [("uid", neteaseUserId.ToString())], userId, ct);
        if (json["playlist"] is not JArray playlists)
        {
            throw new BotApiException(ReadError(json, "获取歌单失败"));
        }

        return playlists
            .Select(playlist => new PlaylistSummary((ulong?)playlist["id"] ?? 0, (string?)playlist["name"] ?? ""))
            .Where(playlist => playlist.Id != 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<Music>> GetPlaylistTracksAsync(ulong playlistId, ulong userId, CancellationToken ct = default)
    {
        var json = await GetAsync("/playlist/track/all", [("id", playlistId.ToString())], userId, ct);
        if (json["songs"] is not JArray songs)
        {
            throw new BotApiException(ReadError(json, "获取歌曲列表失败"));
        }

        return ReadSongs(songs);
    }

    /// <summary>Resolves the NetEase account behind the stored cookie, or null when it expired.</summary>
    public async Task<AccountInfo?> GetAccountAsync(ulong userId, CancellationToken ct = default)
    {
        var json = await GetAsync("/login/status", null, userId, ct);
        var data = json["data"];
        if (data is null || (int?)data["code"] != 200)
        {
            return null;
        }

        return new AccountInfo(
            (ulong?)data["account"]?["id"] ?? 0,
            (string?)data["profile"]?["nickname"] ?? "未知用户");
    }

    /// <summary>Direct audio url for a song, using the requester's account (vip tracks included).</summary>
    public async Task<string> GetSongStreamUrlAsync(ulong songId, ulong userId, CancellationToken ct = default)
    {
        var json = await GetAsync("/song/url", [("id", songId.ToString())], userId, ct);
        var url = (string?)(json["data"] as JArray)?.FirstOrDefault()?["url"];
        if (!string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        throw new BotApiException(await ReadUnplayableReasonAsync(songId, userId, ct));
    }

    public Task<HttpResponseMessage> OpenStreamAsync(string url, CancellationToken ct = default) =>
        _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

    public async Task<LoginResult> LoginByPhoneAsync(string phone, string password, CancellationToken ct = default)
    {
        var json = await GetAsync("/login/cellphone", [("phone", phone), ("password", password)], 0, ct);
        return ReadLogin(json);
    }

    public async Task<LoginResult> LoginByEmailAsync(string email, string password, CancellationToken ct = default)
    {
        var json = await GetAsync("/login", [("email", email), ("password", password)], 0, ct);
        return ReadLogin(json);
    }

    public async Task<LoginResult> LoginByCaptchaAsync(string phone, string captcha, CancellationToken ct = default)
    {
        var json = await GetAsync("/login/cellphone", [("phone", phone), ("captcha", captcha)], 0, ct);
        return ReadLogin(json);
    }

    public async Task SendLoginCaptchaAsync(string phone, CancellationToken ct = default)
    {
        var json = await GetAsync("/captcha/sent", [("phone", phone)], 0, ct);
        if ((int?)json["code"] != 200)
        {
            throw new BotApiException(ReadError(json, "验证码发送失败"));
        }
    }

    public async Task<string> CreateQrKeyAsync(CancellationToken ct = default)
    {
        var json = await GetAsync("/login/qr/key", null, 0, ct);
        var key = (string?)json["data"]?["unikey"];
        return string.IsNullOrWhiteSpace(key) ? throw new BotApiException("创建二维码失败") : key;
    }

    public async Task<byte[]> CreateQrImageAsync(string key, CancellationToken ct = default)
    {
        var json = await GetAsync("/login/qr/create", [("key", key), ("qrimg", "true")], 0, ct);
        var dataUrl = (string?)json["data"]?["qrimg"];
        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            throw new BotApiException("创建二维码失败");
        }

        var payload = dataUrl[(dataUrl.IndexOf(',') + 1)..];
        try
        {
            return Convert.FromBase64String(payload);
        }
        catch (FormatException e)
        {
            throw new BotApiException("二维码解析失败", e);
        }
    }

    public async Task<QrCheckResult> CheckQrAsync(string key, CancellationToken ct = default)
    {
        var json = await GetAsync("/login/qr/check", [("key", key)], 0, ct);
        if ((string?)json["cookie"] is { Length: > 0 } setCookie &&
            UserSessionStore.TryExtractMusicU(setCookie, out var cookie))
        {
            return new QrCheckResult(QrLoginStatus.Confirmed, cookie);
        }

        return new QrCheckResult((int?)json["code"] switch
        {
            800 => QrLoginStatus.Expired,
            802 => QrLoginStatus.Scanned,
            803 => QrLoginStatus.Confirmed,
            _ => QrLoginStatus.Waiting
        });
    }

    public void Dispose() => _http.Dispose();

    private async Task<JObject> GetAsync(
        string path,
        (string Key, string Value)[]? query,
        ulong userId,
        CancellationToken ct)
    {
        var requestUri = BuildUri(path, query);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (userId != 0)
        {
            if (!_sessions.TryGetCookie(userId, out var cookie))
            {
                throw new BotApiException("用户未登录");
            }

            request.Headers.TryAddWithoutValidation("Cookie", $"MUSIC_U={cookie}");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);

        string body;
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new BotApiException($"接口请求失败({(int)response.StatusCode})");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BotApiException("接口请求超时");
        }
        catch (HttpRequestException e)
        {
            throw new BotApiException("无法连接音乐接口", e);
        }

        try
        {
            return JObject.Parse(body);
        }
        catch (Exception e) when (e is not BotApiException)
        {
            throw new BotApiException("接口返回了无法解析的数据", e);
        }
    }

    private Uri BuildUri(string path, (string Key, string Value)[]? query)
    {
        var builder = new StringBuilder(_config.MusicApi).Append(path);
        var separator = '?';

        // The api is cache free but proxies are not; a timestamp keeps responses fresh.
        Append("timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

        foreach (var (key, value) in query ?? [])
        {
            Append(key, value);
        }

        return new Uri(builder.ToString());

        void Append(string key, string value)
        {
            builder.Append(separator).Append(key).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }
    }

    private static IReadOnlyList<Music> ReadSongs(JArray? songs, ulong owner = 0) =>
        songs is null
            ? []
            : songs.Select(song => Music.FromJson(song, owner)).OfType<Music>().ToArray();

    private static LoginResult ReadLogin(JObject json)
    {
        if ((int?)json["code"] != 200)
        {
            throw new BotApiException(ReadError(json, "登录失败"));
        }

        var setCookie = (string?)json["cookie"];
        if (!UserSessionStore.TryExtractMusicU(setCookie, out var cookie))
        {
            throw new BotApiException("服务器返回了错误的数据");
        }

        return new LoginResult(
            cookie,
            (string?)json["profile"]?["nickname"] ?? "未知用户",
            (ulong?)json["account"]?["id"] ?? 0);
    }

    private async Task<string> ReadUnplayableReasonAsync(ulong songId, ulong userId, CancellationToken ct)
    {
        try
        {
            var json = await GetAsync("/check/music", [("id", songId.ToString())], userId, ct);
            var message = (string?)json["message"];
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }
        catch (BotApiException)
        {
            // Fall through to the generic message.
        }

        return "该歌曲暂不可播放";
    }

    private static string ReadError(JObject json, string fallback) =>
        (string?)json["message"] ?? (string?)json["msg"] ?? fallback;
}
