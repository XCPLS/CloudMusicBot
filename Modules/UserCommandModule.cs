using Discord;
using Discord.Interactions;
using Newtonsoft.Json.Linq;

namespace CloudMusicBot.Modules;

[Group("user", "用户操作")]
public class UserCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private static readonly HttpClient HttpClient = new HttpClient();
    
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
        
        var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/search?type=1002&limit=20&keywords={keyword}", Context.User.Id);
        JObject data = JObject.Parse(await response.Content.ReadAsStringAsync());
        if ((int)data["code"] == 200)
        {
            JArray array = (JArray)data["result"]["userprofiles"];
            
            if (array.Count == 0)
            {
                await FollowupAsync("**❌未搜索到用户**");
                return;
            }
            
            List<SelectMenuOptionBuilder> options = new List<SelectMenuOptionBuilder>();
            foreach (var token in array)
            {
                options.Add(new SelectMenuOptionBuilder((string)token["nickname"], (string)token["userId"]));
            }
            
            var menu = new Menu("user", options, "搜索结果", false, Context.User);
            await FollowupAsync(components: menu.GetCurrentPage());
        }
    }
    
    [SlashCommand("me", "查看账号歌单")]
    public async Task MeAsync()
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
        
        var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/login/status", Context.User.Id);
        JObject data = JObject.Parse(await response.Content.ReadAsStringAsync());
        if ((int)data["data"]["code"] == 200)
        {
            ulong id = (ulong)data["data"]["account"]["id"];
            response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/user/playlist?uid={id}", Context.User.Id);
            data = JObject.Parse(await response.Content.ReadAsStringAsync());
            if ((int)data["code"] == 200)
            {
                JArray array = (JArray)data["playlist"];
            
                List<SelectMenuOptionBuilder> options = new List<SelectMenuOptionBuilder>();
                foreach (var token in array)
                {
                    options.Add(new SelectMenuOptionBuilder((string)token["name"], (string)token["id"]));
                }
            
                Menu menu = new Menu("playlist", options, "歌单列表", false, Context.User);
                await FollowupAsync(components: menu.GetCurrentPage());
            }
        }
    }
}