using System.Collections.Specialized;
using System.Web;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;

namespace CloudMusicBot.Modules;

[Group("play", "播放音乐")]
public class PlayCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private static readonly HttpClient HttpClient = new HttpClient()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    
    [SlashCommand("search", "搜索歌曲")]
    public async Task SearchAsync([Summary("keyword", "关键字")] string keyword)
    {
        await DeferAsync(ephemeral: true);
        if (Context.Guild == null)
        {
            await FollowupAsync($"❌请前往 **{Program.MusicChannel.Guild.Name}** 中执行该指令！");
            return;
        }
        
        if (!UserCookie.UserCookies.ContainsKey(Context.User.Id))
        {
            await FollowupAsync("**❌你需要登录后才能执行该操作！**");
            return;
        }

        if (Menu.TryGetUserCurrentMenu(Context.User, out _))
        {
            await FollowupAsync("**❌你还有未处理的表单！**");
            return;
        }
        
        var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/search?limit=20&keywords={keyword}", Context.User.Id);
        JObject data = JObject.Parse(await response.Content.ReadAsStringAsync());
        if ((int)data["code"] == 200)
        {
            JArray array = (JArray)data["result"]["songs"];

            if (array.Count == 0)
            {
                await FollowupAsync("**❌未搜索到歌曲**");
                return;
            }
            
            List<SelectMenuOptionBuilder> options = new List<SelectMenuOptionBuilder>();
            foreach (var token in array)
            {
                options.Add(new SelectMenuOptionBuilder($"{token["name"]} - {MusicUtils.GetArtists(token)}", (string)token["id"], $"《{MusicUtils.GetAlbum(token, out _)}》"));
            }
            
            var menu = new Menu("music", options, "搜索结果", true, Context.User);
            await FollowupAsync(components: menu.GetCurrentPage());
        }
    }

    [SlashCommand("id", "ID点播歌曲")]
    public async Task IdAsync([Summary("id", "歌曲ID")] ulong id)
    {
        await DeferAsync(ephemeral: true);
        
        if (Context.Guild == null)
        {
            await FollowupAsync($"❌请前往 **{Program.MusicChannel.Guild.Name}** 中执行该指令！");
            return;
        }
        
        if (!UserCookie.UserCookies.ContainsKey(Context.User.Id))
        {
            await FollowupAsync("**❌你需要登录后才能执行该操作！**");
            return;
        }
        
        var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/song/detail?ids={id}", Context.User.Id);
        string reply = await response.Content.ReadAsStringAsync();
        
        JObject data = (JObject)(((JArray)(JObject.Parse(reply)["songs"]))[0]);
        if (!data.ContainsKey("al") && !data.ContainsKey("album"))
        {
            await FollowupAsync("**❌未找到对应歌曲**");
            return;
        }
        
        Music music = new Music()
        {
            Id = (ulong)data["id"],
            Name = (string)data["name"],
            Artist = MusicUtils.GetArtists(data),
            Album = MusicUtils.GetAlbum(data, out var cover),
            Cover = cover,
            Owner = Context.User.Id
        };
        
        AudioPlayer.PlayList.Add(music);
        await FollowupAsync("**✔歌曲提交成功**");
        await Program.MusicChannel.SendMessageAsync($"<@{Context.User.Id}> 点播 1 首歌曲: \n```{music.Name} - {music.Artist} 《{music.Album}》```");
    }
    
    [SlashCommand("url", "链接点播歌曲")]
    public async Task UrlAsync([Summary("url", "歌曲链接")] string url)
    {
        await DeferAsync(ephemeral: true);

        if (Context.Guild == null)
        {
            await FollowupAsync($"❌请前往 **{Program.MusicChannel.Guild.Name}** 中执行该指令！");
            return;
        }
        
        if (!UserCookie.UserCookies.ContainsKey(Context.User.Id))
        {
            await FollowupAsync("**❌你需要登录后才能执行该操作！**");
            return;
        }
        
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
        {
            await FollowupAsync("**❌链接解析失败**");
            return;
        }

        NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
        string idText = query["id"];

        if (!ulong.TryParse(idText, out ulong id))
        {
            await FollowupAsync("**❌链接解析失败**");
            return;
        }
        
        
        var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/song/detail?ids={id}", Context.User.Id);
        string reply = await response.Content.ReadAsStringAsync();
        JObject data = (JObject)((JArray)JObject.Parse(reply)["songs"])[0];

        if (!data.ContainsKey("al") && !data.ContainsKey("album"))
        {
            await FollowupAsync("**❌未找到对应歌曲**");
            return;
        }
        
        Music music = new Music()
        {
            Id = (ulong)data["id"],
            Name = (string)data["name"],
            Artist = MusicUtils.GetArtists(data),
            Album = MusicUtils.GetAlbum(data, out var cover),
            Cover = cover,
            Owner = Context.User.Id
        };
        
        AudioPlayer.PlayList.Add(music);
        await FollowupAsync("**✔歌曲提交成功**");
        await Program.MusicChannel.SendMessageAsync($"<@{Context.User.Id}> 点播 1 首歌曲: \n```{music.Name} - {music.Artist} 《{music.Album}》```");
    }
}