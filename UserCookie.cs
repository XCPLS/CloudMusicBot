using System.Text.RegularExpressions;
using Discord;

namespace CloudMusicBot;

public static class UserCookie
{
    private static readonly Dictionary<ulong, string> Cookies = new Dictionary<ulong, string>();

    public static Dictionary<ulong, string> UserCookies => Cookies;

    internal static async Task Init()
    {
        if (!File.Exists("UserCookie.txt"))
        {
            File.Create("UserCookie.txt");
            return;
        }

        foreach (var line in await File.ReadAllLinesAsync("UserCookie.txt"))
        {
            string[] s = line.Split("=", 2);
            Cookies[ulong.Parse(s[0])] = s[1];
        }
        Program.LogMessage(LogSeverity.Info, $"Loaded {Cookies.Count} login cookies.");
    }

    public static void NewUserCookie(ulong user, string cookie)
    {
        Cookies[user] = cookie;
        _ = SaveCookies();
    }

    public static void DeleteCookie(ulong user)
    {
        Cookies.Remove(user);
        _ = SaveCookies();
    }

    public static bool TryParseCookie(string fullCookie, out string cookie)
    { 
        Match match = Regex.Match(fullCookie, "MUSIC_U=([^;]+)");
        if (match.Success)
        {
            cookie = match.Groups[1].Value;
            return true;
        }

        cookie = "";
        return false;
    }

    private static async Task SaveCookies()
    {
        List<string> lines = new List<string>();
        foreach (var kv in Cookies)
        {
            lines.Add($"{kv.Key}={kv.Value}");
        }

        await File.WriteAllLinesAsync("UserCookie.txt", lines);
    }
}