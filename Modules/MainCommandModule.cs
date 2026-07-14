using Discord;
using Discord.Interactions;

namespace CloudMusicBot.Modules;

public class MainCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("logout", "退出网易云账号登录")]
    public async Task LogoutAsync()
    {
        await DeferAsync(ephemeral: true);
        UserCookie.DeleteCookie(Context.User.Id);
        await FollowupAsync("**✔成功退出登录**");
    }
    
    [SlashCommand("queue", "播放列表")]
    public async Task QueueAsync()
    {
        await DeferAsync(ephemeral: true);
        string msg = $"正在播放: {(AudioPlayer.PlayingMusic == null ? "无" : $"{AudioPlayer.PlayingMusic.Name} - {AudioPlayer.PlayingMusic.Artist} 《{AudioPlayer.PlayingMusic.Album}》")}\n```播放列表(共{AudioPlayer.PlayList.Count}首): ";
        foreach (var music in AudioPlayer.PlayList)
        {
            msg += $"\n{music.Name} - {music.Artist} 《{music.Album}》";
        }

        msg += "```";
        await FollowupAsync(msg);
    }

    [SlashCommand("skip", "下一首")]
    public async Task SkipAsync()
    {
        await DeferAsync(ephemeral: true);
        if (Context.Guild == null)
        {
            await FollowupAsync($"❌请前往 **{Program.MusicChannel.Guild.Name}** 中执行该指令！");
            return;
        }
        if (AudioPlayer.PlayingMusic?.Owner != Context.User.Id && Context.User is IGuildUser user && !user.GetPermissions(Program.MusicChannel).ManageChannel)
        {
            await FollowupAsync($"**❌您没有权限执行该指令！**");
            return;
        }
        AudioPlayer.StopPlay();
        await FollowupAsync($"**✔执行成功**");
    }
    
    [SlashCommand("exit", "退出播放")]
    public async Task ExitAsync()
    {
        await DeferAsync(ephemeral: true);
        if (Context.Guild == null)
        {
            await FollowupAsync($"❌请前往 **{Program.MusicChannel.Guild.Name}** 中执行该指令！");
            return;
        }
        if (Context.User is IGuildUser user && !user.GetPermissions(Program.MusicChannel).ManageChannel)
        {
            await FollowupAsync($"**❌您没有权限执行该指令！**");
            return;
        }
        AudioPlayer.Exit();
        await FollowupAsync($"**✔执行成功**");
    }
}