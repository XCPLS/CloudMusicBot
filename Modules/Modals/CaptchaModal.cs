using System.ComponentModel.DataAnnotations;
using Discord.Interactions;

namespace CloudMusicBot.Modules.Modals;

public class CaptchaModal : IModal
{
    public string Title => "登录网易云音乐账号";

    [Required]
    [InputLabel("验证码")]
    [ModalTextInput("captcha", placeholder: "请输入短信验证码", maxLength: 4)]
    public string Code { get; set; }
}