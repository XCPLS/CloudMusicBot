using CloudMusicBot.Modules.Modals;
using Discord;
using Discord.Interactions;
using Newtonsoft.Json.Linq;

namespace CloudMusicBot.Modules;

public class CaptchaModule : InteractionModuleBase<SocketInteractionContext>
{
    private static readonly HttpClient HttpClient = new HttpClient()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    
    internal static readonly Dictionary<ulong, ulong> Phone = new Dictionary<ulong, ulong>();
    
    [ModalInteraction("captcha_modal")]
    public async Task HandleCaptchaModalAsync(CaptchaModal modal)
    {
        await DeferAsync(ephemeral: true);
        var code = modal.Code;
        if (!Phone.TryGetValue(Context.User.Id, out var number))
        {
            await FollowupAsync($"**❌验证超时，请重新登录！**");
            return;
        }
        var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/login/cellphone?phone={number}&captcha={code}");
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
            await FollowupAsync($"**❌登录失败: {data["msg"]}**");
        }
        
        Phone.Remove(Context.User.Id);
    }
}