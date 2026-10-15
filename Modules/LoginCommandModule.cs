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
        try
        {
            await Api.SendLoginCaptchaAsync(phone);
        }
        catch (Exception e)
        {
            BotLog.Warn($"验证码发送失败: {e.Message}");
            await RespondAsync($"**❌验证码发送失败: {e.Message}**", ephemeral: true);
            return;
        }

        await RespondWithModalAsync<CaptchaModal>("captcha_modal");
        Bot.Captcha.Set(Context.User.Id, phone);
    }

    /// <summary>Polls the api in the background; the command handler itself returns right away.</summary>
    private async Task WatchQrLoginAsync(ulong userId, string key)
    {
        using var timeout = new CancellationTokenSource(QrLifetime);
        var scanned = false;

        try
        {
            while (!timeout.IsCancellationRequested)
            {
                await Task.Delay(QrPollInterval, timeout.Token);

                var result = await Api.CheckQrAsync(key, timeout.Token);
                switch (result.Status)
                {
                    case QrLoginStatus.Expired:
                        await UpdateQrStatusAsync("**❌二维码已失效，请重新登录！**");
                        return;

                    case QrLoginStatus.Scanned when !scanned:
                        scanned = true;
                        await UpdateQrStatusAsync("**请在网易云手机客户端点击确认登录**");
                        break;

                    case QrLoginStatus.Confirmed:
                        await CompleteQrLoginAsync(userId, result.Cookie!);
                        return;
                }
            }

            await UpdateQrStatusAsync("**❌二维码已失效，请重新登录！**");
        }
        catch (OperationCanceledException)
        {
            // Timed out or the bot is shutting down.
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
