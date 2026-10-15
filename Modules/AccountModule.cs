using CloudMusicBot.Modules.Modals;
using Discord.Interactions;

namespace CloudMusicBot.Modules;

public class AccountModule : BotModule
{
    public AccountModule(Bot bot) : base(bot)
    {
    }

    [ModalInteraction("account_modal")]
    public async Task HandleAccountModalAsync(AccountModal modal)
    {
        await DeferAsync(ephemeral: true);

        var account = modal.Account.Trim();
        try
        {
            // Only plain ascii digits are a phone number; char.IsDigit would also accept
            // things like full width digits and send them to the wrong endpoint.
            var login = account.All(char.IsAsciiDigit)
                ? await Api.LoginByPhoneAsync(account, modal.Password)
                : await Api.LoginByEmailAsync(account, modal.Password);

            Bot.Sessions.Set(Context.User.Id, login.Cookie);
            await FollowupAsync($"**✔登录成功**\n用户: {login.Nickname}\nID: {login.UserId}");
        }
        catch (BotApiException e)
        {
            await FollowupAsync($"**❌登录失败: {e.Message}**");
        }
        catch (Exception e)
        {
            BotLog.Error($"密码登录失败: {e.Message}");
            await FollowupAsync("**❌登录失败，请稍后重试！**");
        }
    }

    [ModalInteraction("captcha_modal")]
    public async Task HandleCaptchaModalAsync(CaptchaModal modal)
    {
        await DeferAsync(ephemeral: true);

        if (!Bot.Captcha.TryTake(Context.User.Id, out var phone))
        {
            await FollowupAsync("**❌验证超时，请重新登录！**");
            return;
        }

        try
        {
            var login = await Api.LoginByCaptchaAsync(phone, modal.Code);
            Bot.Sessions.Set(Context.User.Id, login.Cookie);
            await FollowupAsync($"**✔登录成功**\n用户: {login.Nickname}\nID: {login.UserId}");
        }
        catch (BotApiException e)
        {
            await FollowupAsync($"**❌登录失败: {e.Message}**");
        }
        catch (Exception e)
        {
            BotLog.Error($"验证码登录失败: {e.Message}");
            await FollowupAsync("**❌登录失败，请稍后重试！**");
        }
    }
}
