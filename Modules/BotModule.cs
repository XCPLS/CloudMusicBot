using Discord;
using Discord.Interactions;

namespace CloudMusicBot.Modules;

/// <summary>
/// Shared precondition checks. Every method answers the user itself and returns
/// false when the command should stop.
/// </summary>
public abstract class BotModule : InteractionModuleBase<SocketInteractionContext>
{
    protected BotModule(Bot bot) => Bot = bot;

    protected Bot Bot { get; }

    protected MusicApi Api => Bot.Api;

    /// <summary>The bot only plays in the guild and voice channel it was configured with.</summary>
    protected async Task<bool> RequireVoiceChannelAsync()
    {
        if (Bot.VoiceChannel is not null && Context.Guild?.Id == Bot.Config.GuildId)
        {
            return true;
        }

        var guild = Bot.Client.GetGuild(Bot.Config.GuildId)?.Name ?? "指定服务器";
        await FollowupAsync($"❌请前往 **{guild}** 中执行该指令！");
        return false;
    }

    protected async Task<bool> RequireLoginAsync()
    {
        if (Bot.Sessions.IsLoggedIn(Context.User.Id))
        {
            return true;
        }

        await FollowupAsync("**❌你需要登录后才能执行该操作！**");
        return false;
    }

    protected async Task<bool> RequireNoOpenMenuAsync()
    {
        if (!Bot.Menus.HasOpenMenu(Context.User.Id))
        {
            return true;
        }

        await FollowupAsync("**❌你还有未处理的表单！**");
        return false;
    }

    /// <summary>The requester of the current song, or anyone who manages the channel.</summary>
    protected async Task<bool> RequirePlaybackOwnerAsync()
    {
        if (Bot.Player.Current?.Owner == Context.User.Id || IsChannelManager())
        {
            return true;
        }

        await FollowupAsync("**❌您没有权限执行该指令！**");
        return false;
    }

    protected async Task<bool> RequireChannelManagerAsync()
    {
        if (IsChannelManager())
        {
            return true;
        }

        await FollowupAsync("**❌您没有权限执行该指令！**");
        return false;
    }

    private bool IsChannelManager() =>
        Context.User is IGuildUser user &&
        Bot.VoiceChannel is { } channel &&
        user.GetPermissions(channel).ManageChannel;
}
