using System.Text;
using CloudMusicBot.Models;

namespace CloudMusicBot.Ui;

public static class TextFormat
{
    /// <summary>Discord limit for a single message.</summary>
    public const int MessageLimit = 2000;

    /// <summary>Discord limit for select menu labels and descriptions.</summary>
    public const int LabelLimit = 100;

    /// <summary>Shortens <paramref name="value"/> so it fits <paramref name="limit"/> characters.</summary>
    public static string Clip(string value, int limit = LabelLimit)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value.Length <= limit ? value : string.Concat(value.AsSpan(0, limit - 1), "…");
    }

    /// <summary>One song per line, cut off before Discord rejects the message.</summary>
    public static string SongList(IEnumerable<Music> songs, int alreadyUsed = 0)
    {
        var builder = new StringBuilder();
        var remaining = songs as IReadOnlyList<Music> ?? songs.ToList();
        var appended = 0;

        foreach (var music in remaining)
        {
            var line = "\n" + music.DisplayName;
            if (builder.Length + line.Length + alreadyUsed > MessageLimit - 1)
            {
                break;
            }

            builder.Append(line);
            appended++;
        }

        if (appended < remaining.Count)
        {
            builder.Append($"\n…还有 {remaining.Count - appended} 首");
        }

        return builder.ToString();
    }
}
