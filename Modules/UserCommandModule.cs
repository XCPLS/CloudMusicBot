using Discord;
using Discord.Interactions;

namespace CloudMusicBot.Modules;

[Group("user", "用户操作")]
public class UserCommandModule : BotModule
{
    public UserCommandModule(Bot bot) : base(bot)
    {
    }

    [SlashCommand("search", "搜索用户")]
    public async Task SearchAsync([Summary("keyword", "关键字")] string keyword)
    {
        await DeferAsync(ephemeral: true);
        if (!await RequireVoiceChannelAsync() || !await RequireLoginAsync() || !await RequireNoOpenMenuAsync())
        {
            return;
        }

        IReadOnlyList<UserSummary> users;
        try
        {
            users = await Api.SearchUsersAsync(keyword, Context.User.Id);
        }
        catch (BotApiException e)
        {
            await FollowupAsync($"**❌搜索失败: {e.Message}**");
            return;
        }

        if (users.Count == 0)
        {
            await FollowupAsync("**❌未搜索到用户**");
            return;
        }

        var options = users.Select(user => Menu.Option(user.Nickname, user.Id.ToString()));
        var menu = Bot.Menus.Open(Context.User.Id, "user", "搜索结果", multiSelect: false, options);
        await FollowupAsync(components: menu.BuildCurrentPage());
    }

    [SlashCommand("me", "查看账号歌单")]
    public async Task MeAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await RequireVoiceChannelAsync() || !await RequireLoginAsync() || !await RequireNoOpenMenuAsync())
        {
            return;
        }

        try
        {
            var account = await Api.GetAccountAsync(Context.User.Id);
            if (account is null)
            {
                await FollowupAsync("**❌登录状态已失效，请重新登录！**");
                return;
            }

            var playlists = await Api.GetUserPlaylistsAsync(account.UserId, Context.User.Id);
            if (playlists.Count == 0)
            {
                await FollowupAsync("**❌未获取到歌单**");
                return;
            }

            var options = playlists.Select(playlist => Menu.Option(playlist.Name, playlist.Id.ToString()));
            var menu = Bot.Menus.Open(Context.User.Id, "playlist", "歌单列表", multiSelect: false, options);
            await FollowupAsync(components: menu.BuildCurrentPage());
        }
        catch (BotApiException e)
        {
            await FollowupAsync($"**❌{e.Message}**");
        }
    }
}
