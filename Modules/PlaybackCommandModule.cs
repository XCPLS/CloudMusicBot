using System.Text;
using Discord.Interactions;

namespace CloudMusicBot.Modules;

public class PlaybackCommandModule : BotModule
{
    public PlaybackCommandModule(Bot bot) : base(bot)
    {
    }

    [SlashCommand("queue", "播放列表")]
    public async Task QueueAsync()
    {
        await DeferAsync(ephemeral: true);

        var current = Bot.Player.Current;
        var queue = Bot.Player.QueueSnapshot();
        var message = new StringBuilder();
        message.Append($"正在播放: {(current is null ? "无" : current.DisplayName)}\n");
        message.Append($"```播放列表(共{queue.Length}首): ");
        message.Append(TextFormat.SongList(queue, message.Length));
        message.Append("```");

        await FollowupAsync(message.ToString());
    }

    [SlashCommand("skip", "下一首")]
    public async Task SkipAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await RequireVoiceChannelAsync() || !await RequirePlaybackOwnerAsync())
        {
            return;
        }

        if (!Bot.Player.Skip())
        {
            await FollowupAsync("**❌当前没有正在播放的歌曲**");
            return;
        }

        await FollowupAsync("**✔执行成功**");
    }

    [SlashCommand("exit", "退出播放")]
    public async Task ExitAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await RequireVoiceChannelAsync() || !await RequireChannelManagerAsync())
        {
            return;
        }

        Bot.Player.Exit();
        await FollowupAsync("**✔执行成功**");
    }
}
