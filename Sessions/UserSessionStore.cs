using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace CloudMusicBot.Sessions;

/// <summary>
/// Discord user id to NetEase <c>MUSIC_U</c> cookie, mirrored to <c>UserCookie.txt</c>.
/// </summary>
public sealed partial class UserSessionStore
{
    private const string FileName = "UserCookie.txt";

    private readonly ConcurrentDictionary<ulong, string> _cookies = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public bool IsLoggedIn(ulong userId) => _cookies.ContainsKey(userId);

    public bool TryGetCookie(ulong userId, out string cookie)
    {
        var found = _cookies.TryGetValue(userId, out var value);
        cookie = value ?? "";
        return found;
    }

    public void Set(ulong userId, string cookie)
    {
        _cookies[userId] = cookie;
        Persist();
    }

    public bool Remove(ulong userId)
    {
        if (!_cookies.TryRemove(userId, out _))
        {
            return false;
        }

        Persist();
        return true;
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(FileName))
        {
            await File.WriteAllTextAsync(FileName, string.Empty);
            return;
        }

        var loaded = 0;
        foreach (var line in await File.ReadAllLinesAsync(FileName))
        {
            // Skip blank lines and anything that is not "<id>=<cookie>".
            var separator = line.IndexOf('=');
            if (separator <= 0 ||
                !ulong.TryParse(line.AsSpan(0, separator), out var userId) ||
                string.IsNullOrWhiteSpace(line[(separator + 1)..]))
            {
                continue;
            }

            _cookies[userId] = line[(separator + 1)..].Trim();
            loaded++;
        }

        BotLog.Info($"已加载 {loaded} 个登录凭证。");
    }

    /// <summary>Extracts the <c>MUSIC_U</c> value from a full Set-Cookie header.</summary>
    public static bool TryExtractMusicU(string? setCookie, out string cookie)
    {
        var match = MusicUCookieRegex().Match(setCookie ?? "");
        cookie = match.Success ? match.Groups[1].Value : "";
        return match.Success;
    }

    private void Persist()
    {
        // Fire and forget: callers are interaction handlers that must not wait on disk,
        // and PersistAsync swallows its own failures.
        _ = PersistAsync();
    }

    private async Task PersistAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            var lines = _cookies.Select(entry => $"{entry.Key}={entry.Value}");
            await File.WriteAllLinesAsync(FileName, lines);
        }
        catch (Exception e)
        {
            BotLog.Error($"保存登录凭证失败: {e.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    [GeneratedRegex("MUSIC_U=([^;]+)")]
    private static partial Regex MusicUCookieRegex();
}
