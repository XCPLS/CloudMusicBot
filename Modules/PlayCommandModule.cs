using System.Text.RegularExpressions;
using Discord;
using Discord.Interactions;

namespace CloudMusicBot.Modules;

[Group("play", "播放音乐")]
public partial class PlayCommandModule : BotModule
{
    public PlayCommandModule(Bot bot) : base(bot)
    {
    }

    [SlashCommand("search", "搜索歌曲")]
    public async Task SearchAsync([Summary("keyword", "关键字")] string keyword)
    {
        await DeferAsync(ephemeral: true);
        if (!await RequireVoiceChannelAsync() || !await RequireLoginAsync() || !await RequireNoOpenMenuAsync())
        {
            return;
        }

        IReadOnlyList<Music> songs;
        try
        {
            songs = await Api.SearchSongsAsync(keyword, Context.User.Id);
        }
        catch (BotApiException e)
        {
            await FollowupAsync($"**❌搜索失败: {e.Message}**");
            return;
        }

        if (songs.Count == 0)
        {
            await FollowupAsync("**❌未搜索到歌曲**");
            return;
        }

        var options = songs.Select(song =>
            Menu.Option(song.Title, song.Id.ToString(), $"《{song.Album}》"));

        var menu = Bot.Menus.Open(Context.User.Id, "music", "搜索结果", multiSelect: true, options);
        await FollowupAsync(components: menu.BuildCurrentPage());
    }

    [SlashCommand("id", "ID点播歌曲")]
    public async Task IdAsync([Summary("id", "歌曲ID")] ulong id)
    {
        await DeferAsync(ephemeral: true);
        if (!await RequireVoiceChannelAsync() || !await RequireLoginAsync())
        {
            return;
        }

        await QueueSongAsync(id);
    }

    [SlashCommand("url", "链接点播歌曲")]
    public async Task UrlAsync([Summary("url", "歌曲链接")] string url)
    {
        await DeferAsync(ephemeral: true);
        if (!await RequireVoiceChannelAsync() || !await RequireLoginAsync())
        {
            return;
        }

        if (!TryExtractSongId(url, out var id))
        {
            await FollowupAsync("**❌链接解析失败**");
            return;
        }

        await QueueSongAsync(id);
    }

    private async Task QueueSongAsync(ulong id)
    {
        Music? song;
        try
        {
            song = await Api.GetSongAsync(id, Context.User.Id);
        }
        catch (BotApiException e)
        {
            await FollowupAsync($"**❌{e.Message}**");
            return;
        }

        if (song is null)
        {
            await FollowupAsync("**❌未找到对应歌曲**");
            return;
        }

        Bot.Player.Enqueue(song);
        await FollowupAsync("**✔歌曲提交成功**");
        await Bot.SendToMusicChannelAsync($"<@{Context.User.Id}> 点播 1 首歌曲: \n```{song.DisplayName}```");
    }

    /// <summary>
    /// Reads the song id out of a share link. The web player keeps the query inside
    /// the fragment (<c>music.163.com/#/song?id=…</c>), so the raw text is scanned.
    /// </summary>
    private static bool TryExtractSongId(string url, out ulong id)
    {
        id = 0;
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return false;
        }

        var match = SongIdRegex().Match(url);
        return match.Success && ulong.TryParse(match.Groups[1].Value, out id);
    }

    [GeneratedRegex(@"[?&#]id=(\d+)")]
    private static partial Regex SongIdRegex();
}
