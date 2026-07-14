using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic.CompilerServices;

namespace CloudMusicBot;

public static class Program
{
    internal static SocketVoiceChannel? MusicChannel;
    internal static ConfigHelper.AppConfig Config;
    
    private static InteractionService _interactions;
    private static IServiceProvider _services;
    private static DiscordSocketClient _client;
    
    public static async Task Main(string[] args)
    {
        Config = ConfigHelper.Load();

        if (string.IsNullOrEmpty(Config.BotToken) || Config.GuildId == 0 || Config.ChannelId == 0 || string.IsNullOrEmpty(Config.MusicApi))
        {
            LogMessage(LogSeverity.Warning, "请前往 **config.json** 完成配置。");
            return;
        }
        
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.All,
            EnableVoiceDaveEncryption = true
        });
        
        _interactions = new InteractionService(_client.Rest);
        _services = new ServiceCollection()
            .AddSingleton(_client)
            .AddSingleton(_interactions)
            .BuildServiceProvider();

        _client.Ready += ReadyAsync;
        _client.Log += LogMessage;
        _client.InteractionCreated += InteractionCreatedAsync;
        
        await _client.LoginAsync(TokenType.Bot, Config.BotToken);
        await _client.StartAsync();
        await Task.Delay(-1);
    }
    
    private static async Task ReadyAsync()
    {
        await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
        await _interactions.RegisterCommandsGloballyAsync();
        await UserCookie.Init();
        AudioPlayer.Init();
        MusicChannel = _client.GetGuild(Config.GuildId).GetVoiceChannel(Config.ChannelId);
        if (MusicChannel == null)
        {
            LogMessage(LogSeverity.Warning, "语音频道获取失败，请检查配置文件！");
            await _client.LogoutAsync();
            Environment.Exit(0);
        }
    }
    
    private static async Task InteractionCreatedAsync(SocketInteraction interaction)
    {
        var ctx = new SocketInteractionContext(_client, interaction);
        await _interactions.ExecuteCommandAsync(ctx, _services);
    }

    private static Task LogMessage(LogMessage message)
    {
        Console.WriteLine($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] [{message.Severity}] {message.Message}");
        return Task.CompletedTask;
    }

    internal static void LogMessage(LogSeverity level, string msg)
    {
        Console.WriteLine($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] [{level}] {msg}");
    }
}