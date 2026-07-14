using System.ComponentModel.DataAnnotations;
using Discord.Interactions;

namespace CloudMusicBot.Modules.Modals;

public class AccountModal : IModal
{
    public string Title => "登录网易云音乐账号";

    [Required]
    [InputLabel("账号")]
    [ModalTextInput("account", placeholder: "邮箱/手机号")]
    public string Account { get; set; }
    
    [Required]
    [InputLabel("密码")]
    [ModalTextInput("password", placeholder: "密码")]
    public string Password { get; set; }
}