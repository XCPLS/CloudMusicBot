using Discord;
using Discord.Interactions;

namespace CloudMusicBot.Modules;

public class MenuInteractionModule : BotModule
{
    private const string Expired = "**❌该操作已过期**";

    /// <summary>An edit with this component set removes the buttons from the message.</summary>
    private static readonly MessageComponent EmptyComponents = new ComponentBuilder().Build();

    public MenuInteractionModule(Bot bot) : base(bot)
    {
    }

    [ComponentInteraction("menu_previous")]
    public async Task PreviousAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!Bot.Menus.TryGet(Context.User.Id, out var menu))
        {
            await CloseAsync(Expired);
            return;
        }

        menu.Previous();
        await ModifyOriginalResponseAsync(message => message.Components = menu.BuildCurrentPage());
    }

    [ComponentInteraction("menu_next")]
    public async Task NextAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!Bot.Menus.TryGet(Context.User.Id, out var menu))
        {
            await CloseAsync(Expired);
            return;
        }

        menu.Next();
        await ModifyOriginalResponseAsync(message => message.Components = menu.BuildCurrentPage());
    }

    [ComponentInteraction("menu_cancel")]
    public async Task CancelAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!Bot.Menus.TryGet(Context.User.Id, out _))
        {
            await CloseAsync(Expired);
            return;
        }

        Bot.Menus.Close(Context.User.Id);
        await CloseAsync("**✔该操作已取消**");
    }

    [ComponentInteraction("music")]
    public async Task MusicSelectAsync(string[] selectedValues)
    {
        await DeferAsync(ephemeral: true);
        if (!Bot.Menus.TryGet(Context.User.Id, out var menu))
        {
            await CloseAsync(Expired);
            return;
        }

        menu.UpdateSelection(selectedValues);
        // Re-render so the picks survive a trip to another page and back.
        await ModifyOriginalResponseAsync(message => message.Components = menu.BuildCurrentPage());
    }

    [ComponentInteraction("menu_music_confirm")]
    public async Task MusicConfirmAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!Bot.Menus.TryGet(Context.User.Id, out var menu))
        {
            await CloseAsync(Expired);
            return;
        }

        var selected = menu.GetSelected();
        Bot.Menus.Close(Context.User.Id);

        if (selected.Length == 0)
        {
            await CloseAsync("**❌你还没有选择歌曲**");
            return;
        }

        try
        {
            var songIds = selected.Select(ulong.Parse);
            var songs = await Api.GetSongsAsync(songIds, Context.User.Id);
            if (songs.Count == 0)
            {
                await CloseAsync("**❌未找到对应歌曲**");
                return;
            }

            Bot.Player.EnqueueRange(songs);
            await CloseAsync("**✔歌曲提交成功**");

            // The header has to come out of the same budget as the list itself.
            var header = $"<@{Context.User.Id}> 点播 {songs.Count} 首歌曲: \n```";
            await Bot.SendToMusicChannelAsync(
                $"{header}{TextFormat.SongList(songs, header.Length).TrimStart('\n')}```");
        }
        catch (BotApiException e)
        {
            await CloseAsync($"**❌{e.Message}**");
        }
    }

    [ComponentInteraction("user")]
    public async Task UserSelectAsync(string selectedValue)
    {
        await DeferAsync(ephemeral: true);
        if (!Bot.Menus.TryGet(Context.User.Id, out _))
        {
            await CloseAsync(Expired);
            return;
        }

        Bot.Menus.Close(Context.User.Id);
        await CloseAsync("**加载中...**");

        if (!ulong.TryParse(selectedValue, out var userId))
        {
            await CloseAsync("**❌该操作已过期**");
            return;
        }

        try
        {
            var playlists = await Api.GetUserPlaylistsAsync(userId, Context.User.Id);
            if (playlists.Count == 0)
            {
                await CloseAsync("**❌该用户未公开歌单**");
                return;
            }

            var options = playlists.Select(playlist => Menu.Option(playlist.Name, playlist.Id.ToString()));
            await OpenMenuAsync("playlist", "歌单列表", multiSelect: false, options);
        }
        catch (BotApiException e)
        {
            await CloseAsync($"**❌{e.Message}**");
        }
    }

    [ComponentInteraction("playlist")]
    public async Task PlaylistSelectAsync(string selectedValue)
    {
        await DeferAsync(ephemeral: true);
        if (!Bot.Menus.TryGet(Context.User.Id, out _))
        {
            await CloseAsync(Expired);
            return;
        }

        Bot.Menus.Close(Context.User.Id);
        await CloseAsync("**加载中...**");

        if (!ulong.TryParse(selectedValue, out var playlistId))
        {
            await CloseAsync("**❌该操作已过期**");
            return;
        }

        try
        {
            var songs = await Api.GetPlaylistTracksAsync(playlistId, Context.User.Id);
            if (songs.Count == 0)
            {
                await CloseAsync("**❌该歌单没有歌曲**");
                return;
            }

            var options = songs.Select(song =>
                Menu.Option(song.Title, song.Id.ToString(), $"《{song.Album}》"));
            await OpenMenuAsync("music", "歌曲列表", multiSelect: true, options);
        }
        catch (BotApiException e)
        {
            await CloseAsync($"**❌{e.Message}**");
        }
    }

    private async Task OpenMenuAsync(string customId, string placeholder, bool multiSelect, IEnumerable<SelectMenuOptionBuilder> options)
    {
        var menu = Bot.Menus.Open(Context.User.Id, customId, placeholder, multiSelect, options);
        await ModifyOriginalResponseAsync(message =>
        {
            message.Content = "";
            message.Components = menu.BuildCurrentPage();
        });
    }

    /// <summary>Replaces the menu message with a plain result.</summary>
    private async Task CloseAsync(string content)
    {
        await ModifyOriginalResponseAsync(message =>
        {
            message.Content = content;
            message.Components = EmptyComponents;
        });
    }
}
