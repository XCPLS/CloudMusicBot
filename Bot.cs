using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace CloudMusicBot;

/// <summary>Application root: owns the Discord client and every shared service.</summary>
public sealed class Bot : IAsyncDisposable
{
    private const int VoiceChannelAttempts = 5;

    private static readonly TimeSpan VoiceChannelRetryDelay = TimeSpan.FromSeconds(2);

    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly ServiceProvider _services;
    private readonly TaskCompletionSource _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _initialized;

    public Bot(AppConfig config)
    {
        Config = config;
        Sessions = new UserSessionStore();
        Captcha = new CaptchaChallengeStore();
        Api = new MusicApi(config, Sessions);
        Menus = new MenuManager();
        Player = new AudioPlayer(this);

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            // Only the intents actually used. The privileged ones (message content,
            // members, presences) stay off, which keeps the bot working even when
            // they are disabled for the application in the developer portal.
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates,
            EnableVoiceDaveEncryption = true
        });

        _interactions = new InteractionService(_client.Rest);
        _services = new ServiceCollection().AddSingleton(this).BuildServiceProvider();
    }

    public AppConfig Config { get; }

    public MusicApi Api { get; }

    public UserSessionStore Sessions { get; }

    public CaptchaChallengeStore Captcha { get; }

    public MenuManager Menus { get; }

    public AudioPlayer Player { get; }

    public DiscordSocketClient Client => _client;

    public SocketVoiceChannel? VoiceChannel { get; private set; }

    public async Task RunAsync()
    {
        _client.Log += BotLog.HandleAsync;
        _client.Ready += OnReadyAsync;
        _client.InteractionCreated += OnInteractionCreatedAsync;

        await _client.LoginAsync(TokenType.Bot, Config.BotToken);
        await _client.StartAsync();

        await _shutdown.Task;

        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Player.DisposeAsync();
        Api.Dispose();
        await _client.DisposeAsync();
        _interactions.Dispose();
        await _services.DisposeAsync();
    }

    private async Task OnReadyAsync()
    {
        // Ready fires again after every reconnect; modules must be added only once.
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            await Sessions.LoadAsync();
            await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
            await _interactions.RegisterCommandsGloballyAsync();

            var channel = await ResolveVoiceChannelAsync();
            if (channel is null)
            {
                BotLog.Warn("语音频道获取失败，请检查 config.json 中的 GuildId / ChannelId。");
                _shutdown.TrySetResult();
                return;
            }

            VoiceChannel = channel;
            BotLog.Info($"已就绪: {_client.CurrentUser.Username} @ {channel.Guild.Name} / {channel.Name}");
        }
        catch (Exception e)
        {
            _initialized = false;
            BotLog.Error($"初始化失败: {e}");
        }
    }

    private async Task<SocketVoiceChannel?> ResolveVoiceChannelAsync()
    {
        for (var attempt = 1; attempt <= VoiceChannelAttempts; attempt++)
        {
            var channel = _client.GetGuild(Config.GuildId)?.GetVoiceChannel(Config.ChannelId);
            if (channel is not null)
            {
                return channel;
            }

            await Task.Delay(VoiceChannelRetryDelay);
        }

        return null;
    }

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(_client, interaction);
            var result = await _interactions.ExecuteCommandAsync(context, _services);
            if (result.IsSuccess)
            {
                return;
            }

            BotLog.Error($"指令执行失败({interaction.Type}): {result.ErrorReason}");
            await ReplyAsync(interaction, "**❌指令执行失败，请稍后重试！**");
        }
        catch (Exception e)
        {
            BotLog.Error($"处理交互时发生异常: {e}");
        }
    }

    private static async Task ReplyAsync(SocketInteraction interaction, string message)
    {
        try
        {
            if (interaction.HasResponded)
            {
                await interaction.FollowupAsync(message, ephemeral: true);
            }
            else
            {
                await interaction.RespondAsync(message, ephemeral: true);
            }
        }
        catch (Exception e)
        {
            BotLog.Error($"回复错误提示失败: {e.Message}");
        }
    }

    /// <summary>Sends to the text chat attached to the music voice channel.</summary>
    public async Task SendToMusicChannelAsync(string text)
    {
        if (VoiceChannel is { } channel)
        {
            await channel.SendMessageAsync(text);
        }
    }
}
