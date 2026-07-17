using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CloudMusicBot.Modules.Modals;
using Discord;
using Discord.Interactions;
using Newtonsoft.Json.Linq;

namespace CloudMusicBot.Modules;

[Group("login", "登录网易云账号")]
public class LoginCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private static readonly HttpClient HttpClient = new HttpClient();
    private static readonly Dictionary<ulong, string> QrKey = new Dictionary<ulong, string>();
    
    [SlashCommand("qr", "二维码登录")]
    public async Task LoginQrAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!QrKey.TryGetValue(Context.User.Id, out var key))
        {
            var keyResponse = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/login/qr/key");
            JObject json = JObject.Parse(await keyResponse.Content.ReadAsStringAsync());
            if ((int) json["code"] == 200)
            {
                key = (string) json["data"]["unikey"];
                QrKey[Context.User.Id] = key;
            }
            else
            {
                await FollowupAsync($"**❌创建二维码失败，请稍后重试！**");
                return;
            }
        }
        var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/login/qr/create?key={key}&qrimg=true");
        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        if ((int) data["code"] == 200)
        {
            string base64 = ((string)data["data"]["qrimg"]).Split(",", 2)[1];
            byte[] b = Convert.FromBase64String(base64);
            var ms = new MemoryStream(b);
            await FollowupWithFileAsync(ms, "qr.png", "**↓请扫描下方二维码登录↓**", ephemeral: true);
            await ms.DisposeAsync();
            _ = QrLoginTask(Context.User.Id);
        }
        else
        {
            await FollowupAsync($"**❌创建二维码失败，请稍后重试！**");
        }
    }

    private async Task QrLoginTask(ulong uid)
    {
        bool scanned = false;
        while (true)
        {
            var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/login/qr/check?key={QrKey[uid]}");
            JObject data = JObject.Parse(await response.Content.ReadAsStringAsync());
            int code = (int)data["code"];
            
            if (code == 800)
            {
                await ModifyOriginalResponseAsync(x =>
                {
                    x.Content = "**❌二维码已失效，请重新登录！**";
                    x.Attachments = Array.Empty<FileAttachment>();
                });
                break;
            }
            if (code == 802 && !scanned)
            {
                scanned = true;
                await ModifyOriginalResponseAsync(x =>
                {
                    x.Content = "**请在网易云手机客户端点击确认登录**";
                    x.Attachments = Array.Empty<FileAttachment>();
                });
            } 
            else if (code == 803)
            {
                if (UserCookie.TryParseCookie((string)data["cookie"], out var cookie))
                {
                    UserCookie.NewUserCookie(uid, cookie);
                    
                    response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/login/status", uid);
                    data = JObject.Parse(await response.Content.ReadAsStringAsync());
                    string nick = (string) data["data"]["profile"]["nickname"];
                    ulong cloudMusicUserid = (ulong) data["data"]["account"]["id"];
                    await ModifyOriginalResponseAsync(x =>
                    {
                        x.Content = $"**✔登录成功**\n用户: {nick}\nID: {cloudMusicUserid}";
                        x.Attachments = Array.Empty<FileAttachment>();
                    });
                }
                else
                {
                    await ModifyOriginalResponseAsync(x =>
                    {
                        x.Content = "**❌服务器返回了错误的数据！**";
                        x.Attachments = Array.Empty<FileAttachment>();
                    });
                }
                break;
            }
            
            await Task.Delay(1000);
        }
        
        QrKey.Remove(uid);
    }
    
    [SlashCommand("password", "密码登录")]
    public async Task LoginPasswordAsync()
    {
        await RespondWithModalAsync<AccountModal>("account_modal");
    }
    
    [SlashCommand("captcha", "验证码登录")]
    public async Task LoginCaptchaAsync([Summary("phone", "手机号")] ulong number)
    {
        var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/captcha/sent?phone={number}");
        JObject data = JObject.Parse(await response.Content.ReadAsStringAsync());
        if ((int) data["code"] == 200)
        {
            await RespondWithModalAsync<CaptchaModal>("captcha_modal");
            CaptchaModule.Phone[Context.User.Id] = number;
            for (int i = 0; i < 120; i++)
            {
                if (!CaptchaModule.Phone.ContainsKey(Context.User.Id))
                {
                    return;
                }
                await Task.Delay(5000);
            }

            CaptchaModule.Phone.Remove(Context.User.Id);
        }
        else
        {
            await RespondAsync($"**❌验证码发送失败: {data["message"]}**", ephemeral: true);
        }
    }
}