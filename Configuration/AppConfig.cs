using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CloudMusicBot.Configuration;

/// <summary>Contents of <c>config.json</c>.</summary>
public sealed class AppConfig
{
    public ulong GuildId { get; set; }

    public ulong ChannelId { get; set; }

    public string BotToken { get; set; } = "";

    public string MusicApi { get; set; } = "";

    /// <summary>ffmpeg executable, either a command on PATH or an absolute path.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(BotToken))
        {
            error = "BotToken 未配置";
            return false;
        }

        if (GuildId == 0)
        {
            error = "GuildId 未配置";
            return false;
        }

        if (ChannelId == 0)
        {
            error = "ChannelId 未配置";
            return false;
        }

        if (!Uri.TryCreate(MusicApi, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "MusicApi 无效";
            return false;
        }

        error = "";
        return true;
    }
}

public static class ConfigStore
{
    private const string ConfigPath = "config.json";

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented
    };

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaults = new AppConfig();
            Save(defaults);
            return defaults;
        }

        AppConfig config;
        try
        {
            var json = JObject.Parse(File.ReadAllText(ConfigPath));

            // Early versions documented the voice channel as "Channel"; keep reading it.
            if (json["ChannelId"] is null && json["Channel"] is not null)
            {
                json["ChannelId"] = json["Channel"];
            }

            config = json.ToObject<AppConfig>() ?? new AppConfig();
        }
        catch (Exception e)
        {
            BotLog.Warn($"config.json 解析失败({e.Message})，已使用默认配置。");
            return new AppConfig();
        }

        config.MusicApi = config.MusicApi.TrimEnd('/');
        config.FfmpegPath = string.IsNullOrWhiteSpace(config.FfmpegPath) ? "ffmpeg" : config.FfmpegPath.Trim();
        return config;
    }

    public static void Save(AppConfig config)
    {
        File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(config, JsonSettings));
    }
}
