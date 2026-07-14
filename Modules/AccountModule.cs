using CloudMusicBot.Modules.Modals;
using Discord.Interactions;
using Newtonsoft.Json.Linq;

namespace CloudMusicBot.Modules;

public class AccountModule : InteractionModuleBase<SocketInteractionContext>
{
    private static readonly HttpClient HttpClient = new HttpClient()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    
    [ModalInteraction("account_modal")]
    public async Task HandleCaptchaModalAsync(AccountModal modal)
    {
        await DeferAsync(ephemeral: true);
        string url;
        if (ulong.TryParse(modal.Account, out var number))
        {
            url = $"{Program.Config.MusicApi}/login/cellphone?phone={number}&password={modal.Password}";
        }
        else
        {
            url = $"{Program.Config.MusicApi}/login?email={number}&password={modal.Password}";
        }
        var response = await HttpClient.GetAsyncWithTimestamp(url);
        JObject data = JObject.Parse(await response.Content.ReadAsStringAsync());
        if ((int)data["code"] == 200)
        {
            if (UserCookie.TryParseCookie((string)data["cookie"], out var cookie))
            {
                UserCookie.NewUserCookie(Context.User.Id, cookie);
                await FollowupAsync($"**✔登录成功**\n用户: {data["profile"]["nickname"]}\nID: {data["account"]["id"]}");
            }
            else
            {
                await FollowupAsync($"**❌服务器返回了错误的数据！**");
            }
        }
        else
        {
            await FollowupAsync($"**❌登录失败: {data["message"]}**");
        }
    }
}