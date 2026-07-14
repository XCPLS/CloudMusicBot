using System.Text.Json;
using Newtonsoft.Json;

namespace CloudMusicBot;

public static class ConfigHelper
{
    private const string ConfigPath = "config.json";
    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented
    };
    
    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = new AppConfig();
            Save(defaultConfig);
            return defaultConfig;
        }

        string text = File.ReadAllText(ConfigPath);
        return JsonConvert.DeserializeObject<AppConfig>(text, JsonSettings);
    }
    
    public static void Save(AppConfig config)
    {
        string json = JsonConvert.SerializeObject(config, JsonSettings);
        File.WriteAllText(ConfigPath, json);
    }
    
    public class AppConfig
    {
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public string BotToken { get; set; } = "";
        public string MusicApi { get; set; } = "";
    }
}