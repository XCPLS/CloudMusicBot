using Newtonsoft.Json.Linq;

namespace CloudMusicBot.Models;

public sealed class Music
{
    public required ulong Id { get; init; }

    public string Name { get; init; } = "未知歌曲";

    public string Artist { get; init; } = "未知歌手";

    public string Album { get; init; } = "未知专辑";

    public string Cover { get; init; } = "";

    /// <summary>Discord user that queued this song; used to pick the NetEase cookie.</summary>
    public ulong Owner { get; init; }

    /// <summary>"歌名 - 歌手"</summary>
    public string Title => $"{Name} - {Artist}";

    /// <summary>"歌名 - 歌手 《专辑》"</summary>
    public string DisplayName => $"{Title} 《{Album}》";

    /// <summary>
    /// Maps a song object from an api response. Both response shapes are supported:
    /// the current one (<c>ar</c>/<c>al</c>) and the legacy one (<c>artists</c>/<c>album</c>).
    /// </summary>
    public static Music? FromJson(JToken token, ulong owner = 0)
    {
        if (token is not JObject song)
        {
            return null;
        }

        var id = (ulong?)song["id"];
        if (id is null or 0)
        {
            return null;
        }

        var (album, cover) = ReadAlbum(song);
        return new Music
        {
            Id = id.Value,
            Name = Fallback((string?)song["name"], "未知歌曲"),
            Artist = ReadArtists(song),
            Album = album,
            Cover = cover,
            Owner = owner
        };
    }

    private static string ReadArtists(JObject song)
    {
        var artists = song["ar"] as JArray ?? song["artists"] as JArray;
        if (artists is null)
        {
            return "未知歌手";
        }

        var names = artists
            .Select(artist => (string?)artist?["name"])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        return names.Length == 0 ? "未知歌手" : string.Join(" / ", names);
    }

    private static (string Album, string Cover) ReadAlbum(JObject song)
    {
        if (song["al"] is JObject al)
        {
            return (Fallback((string?)al["name"], "未知专辑"), (string?)al["picUrl"] ?? "");
        }

        if (song["album"] is JObject album)
        {
            var cover = (string?)album.SelectToken("artist.img1v1url") ?? "";
            return (Fallback((string?)album["name"], "未知专辑"), cover);
        }

        return ("未知专辑", "");
    }

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
