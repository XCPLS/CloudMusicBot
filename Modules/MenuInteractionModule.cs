using Discord;
using Discord.Interactions;
using Newtonsoft.Json.Linq;

namespace CloudMusicBot.Modules;

public class MenuInteractionModule : InteractionModuleBase<SocketInteractionContext>
{
    private static readonly HttpClient HttpClient = new HttpClient()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    
    [ComponentInteraction("menu_previous")]
    public async Task PreviousAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!Menu.TryGetUserCurrentMenu(Context.User, out var menu))
        {
            await RespondAsync("**❌该操作已过期**", ephemeral: true);
            return;
        }
        menu.Previous();
        await ModifyOriginalResponseAsync(x =>
        {
            x.Components = menu.GetCurrentPage();
        });
    }
    
    [ComponentInteraction("menu_next")]
    public async Task NextAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!Menu.TryGetUserCurrentMenu(Context.User, out var menu))
        {
            await RespondAsync("**❌该操作已过期**", ephemeral: true);
            return;
        }
        menu.Next();
        await ModifyOriginalResponseAsync(x =>
        {
            x.Components = menu.GetCurrentPage();
        });
    }
    
    [ComponentInteraction("menu_cancel")]
    public async Task CancelAsync()
    {
        await DeferAsync(ephemeral: true);
        
        if (!Menu.TryGetUserCurrentMenu(Context.User, out var menu))
        {
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = "**❌该操作已过期**";
                x.Components = null;
            });
            return;
        }
        menu.Close();
        await ModifyOriginalResponseAsync(x =>
        {
            x.Content = "**✔该操作已取消**";
            x.Components = null;
        });
    }
    
    [ComponentInteraction("music")]
    public async Task MusicSelectAsync(string[] selectedValue)
    {
        await DeferAsync(ephemeral: true);
        if (!Menu.TryGetUserCurrentMenu(Context.User, out var menu))
        {
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = "**❌该操作已过期**";
                x.Components = null;
            });
            return;
        }
        menu.UpdateSelect(selectedValue);
        await ModifyOriginalResponseAsync(x =>
        {
            
        });
    }
    
    [ComponentInteraction("menu_music_confirm")]
    public async Task MusicConfirmAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!Menu.TryGetUserCurrentMenu(Context.User, out var menu))
        {
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = "**❌该操作已过期**";
                x.Components = null;
            });
            return;
        }
        menu.Close();
        List<Music> select = new List<Music>();
        var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/song/detail?ids={string.Join(",", menu.GetSelected())}", Context.User.Id);
        string reply = await response.Content.ReadAsStringAsync();
        foreach (var song in (JArray)JObject.Parse(reply)["songs"])
        {
            JObject data = (JObject)song;
            select.Add(new Music()
            {
                Id = (ulong) data["id"],
                Name = (string) data["name"],
                Artist = MusicUtils.GetArtists(song),
                Album = MusicUtils.GetAlbum(song, out var cover),
                Cover = cover,
                Owner = Context.User.Id
            });
        }
        
        AudioPlayer.PlayList.AddRange(select);
        await ModifyOriginalResponseAsync(x =>
        {
            x.Content = "**✔歌曲提交成功**";
            x.Components = null;
        });
        await Program.MusicChannel.SendMessageAsync($"<@{Context.User.Id}> 点播 {select.Count} 首歌曲: \n```{string.Join("\n", select.Select(music => $"{music.Name} - {music.Artist} 《{music.Album}》"))}```");
        
    }
    
    [ComponentInteraction("user")]
    public async Task UserSelectAsync(string selectedValue)
    {
        await DeferAsync(ephemeral: true);
        if (!Menu.TryGetUserCurrentMenu(Context.User, out var menu))
        {
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = "**❌该操作已过期**";
                x.Components = null;
            });
            return;
        }
        menu.Close();
        var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/user/playlist?uid={selectedValue}&limit=100", Context.User.Id);
        JObject data = JObject.Parse(await response.Content.ReadAsStringAsync());
        if ((int)data["code"] == 200)
        {
            JArray array = (JArray)data["playlist"];
            
            if (array.Count == 0)
            {
                await ModifyOriginalResponseAsync(x =>
                {
                    x.Content = "**❌该用户未公开歌单**";
                    x.Components = null;
                });
                return;
            }
            
            List<SelectMenuOptionBuilder> options = new List<SelectMenuOptionBuilder>();
            foreach (var token in array)
            {
                options.Add(new SelectMenuOptionBuilder((string)token["name"], (string)token["id"]));
            }
            
            menu = new Menu("playlist", options, "歌单列表", false, Context.User);
            await ModifyOriginalResponseAsync(x =>
            {
                x.Components = menu.GetCurrentPage();
            });
        }
    }
    
    [ComponentInteraction("playlist")]
    public async Task PlayListSelectAsync(string selectedValue)
    {
        await DeferAsync(ephemeral: true);
        if (!Menu.TryGetUserCurrentMenu(Context.User, out var menu))
        {
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = "**❌该操作已过期**";
                x.Components = null;
            });
            return;
        }
        menu.Close();
        var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/playlist/track/all?id={selectedValue}", Context.User.Id);
        JObject data = JObject.Parse(await response.Content.ReadAsStringAsync());
        if ((int)data["code"] == 200)
        {
            JArray array = (JArray)data["songs"];
            List<SelectMenuOptionBuilder> options = new List<SelectMenuOptionBuilder>();
            
            foreach (var token in array)
            {
                options.Add(new SelectMenuOptionBuilder($"{token["name"]} - {MusicUtils.GetArtists(token)}", (string)token["id"], $"《{MusicUtils.GetAlbum(token, out _)}》"));
            }
            
            menu = new Menu("music", options, "歌曲列表", true, Context.User);
            await ModifyOriginalResponseAsync(x =>
            {
                x.Components = menu.GetCurrentPage();
            });
        }
    }
}