using System.Collections.Concurrent;
using CloudMusicBot.Modules.Modals;
using Discord;
using Discord.Interactions;

namespace CloudMusicBot.Modules;

[Group("login", "登录网易云账号")]
public class LoginCommandModule : BotModule
{
    private static readonly TimeSpan QrLifetime = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan QrPollInterval = TimeSpan.FromSeconds(2);

    private const string QrExpired = "**❌二维码已失效，请重新登录！**";

    /// <summary>Pending qr keys, so asking again shows the same code instead of a new one.</summary>
    private static readonly ConcurrentDictionary<ulong, string> QrKeys = new();

    public LoginCommandModule(Bot bot) : base(bot)
    {
    }

    [SlashCommand("logout", "退出网易云账号登录")]
    public async Task LogoutAsync()
    {
        await DeferAsync(ephemeral: true);
        Bot.Sessions.Remove(Context.User.Id);
        await FollowupAsync("**✔成功退出登录**");
    }

    [SlashCommand("qr", "二维码登录")]
    public async Task LoginQrAsync()
    {
        await DeferAsync(ephemeral: true);

        string key;
        try
        {
            key = QrKeys.TryGetValue(Context.User.Id, out var pending)
                ? pending
                : QrKeys[Context.User.Id] = await Api.CreateQrKeyAsync();
        }
        catch (BotApiException e)
        {
            await FollowupAsync($"**❌{e.Message}**");
            return;
        }

        byte[] image;
        try
        {
            image = await Api.CreateQrImageAsync(key);
        }
        catch (BotApiException e)
        {
            // The watcher that normally drops the key never started, so a dead key would
            // otherwise be handed back to the next /login qr and expire immediately.
            QrKeys.TryRemove(Context.User.Id, out _);
            await FollowupAsync($"**❌{e.Message}**");
            return;
        }

        await ModifyOriginalResponseAsync(message =>
        {
            message.Content = "**↓请扫描下方二维码登录↓**";
            message.Attachments = new[] { new FileAttachment(new MemoryStream(image), "qr.png") };
        });

        _ = WatchQrLoginAsync(Context.User.Id, key);
    }

    [SlashCommand("password", "密码登录")]
    public Task LoginPasswordAsync() => RespondWithModalAsync<AccountModal>("account_modal");

    [SlashCommand("captcha", "验证码登录")]
    public async Task LoginCaptchaAsync([Summary("phone", "手机号")] string phone)
    {
        // The modal is this command's first response and Discord only allows three
        // seconds for that, which sending the code can easily outlast. So the modal goes
        // up first and the code is requested behind it, with a failure reported after.
        Bot.Captcha.Set(Context.User.Id, phone);
        await RespondWithModalAsync<CaptchaModal>("captcha_modal");

        try
        {
            await Api.SendLoginCaptchaAsync(phone);
        }
        catch (Exception e)
        {
            BotLog.Warn($"验证码发送失败: {e.Message}");
            try
            {
                await FollowupAsync($"**❌验证码发送失败: {e.Message}**", ephemeral: true);
            }
            catch (Exception followup)
            {
                BotLog.Warn($"提示验证码发送失败时出错: {followup.Message}");
            }
        }
    }

    /// <summary>Polls the api in the background; the command handler itself returns right away.</summary>
    private async Task WatchQrLoginAsync(ulong userId, string key)
    {
        using var timeout = new CancellationTokenSource(QrLifetime);
        var scanned = false;

        try
        {
            while (true)
            {
                await Task.Delay(QrPollInterval, timeout.Token);

                var result = await Api.CheckQrAsync(key, timeout.Token);
                switch (result.Status)
                {
                    case QrLoginStatus.Expired:
                        await UpdateQrStatusAsync(QrExpired);
                        return;

                    case QrLoginStatus.Scanned when !scanned:
                        scanned = true;
                        await UpdateQrStatusAsync("**请在网易云手机客户端点击确认登录**");
                        break;

                    case QrLoginStatus.Confirmed:
                        // The cookie only shows up once the login really went through;
                        // without it there is nothing to store, so keep polling.
                        if (result.Cookie is not { Length: > 0 } cookie)
                        {
                            break;
                        }

                        await CompleteQrLoginAsync(userId, cookie);
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The window closed inside one of the awaits. The code on screen is dead now
            // and would otherwise sit there looking scannable forever.
            await UpdateQrStatusAsync(QrExpired);
        }
        catch (Exception e)
        {
            BotLog.Error($"二维码登录失败: {e.Message}");
            await UpdateQrStatusAsync("**❌登录失败，请稍后重试！**");
        }
        finally
        {
            QrKeys.TryRemove(userId, out _);
        }
    }

    private async Task CompleteQrLoginAsync(ulong userId, string cookie)
    {
        Bot.Sessions.Set(userId, cookie);
        var account = await Api.GetAccountAsync(userId);
        await UpdateQrStatusAsync(
            $"**✔登录成功**\n用户: {account?.Nickname ?? "未知用户"}\nID: {account?.UserId ?? 0}");
    }

    private async Task UpdateQrStatusAsync(string content)
    {
        try
        {
            await ModifyOriginalResponseAsync(message =>
            {
                message.Content = content;
                message.Attachments = Array.Empty<FileAttachment>();
            });
        }
        catch (Exception e)
        {
            BotLog.Warn($"更新二维码状态失败: {e.Message}");
        }
    }
}
