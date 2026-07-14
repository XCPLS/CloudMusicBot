# CloudMusicBot

CloudMusicBot 是一个使用 C# 开发的 Discord 音乐机器人项目，用于在 Discord 语音房中播放网易云音乐。

## 功能介绍

- 登录 / 登出网易云音乐账号
- 搜索歌曲 / 用户
- 支持通过 id / url 点歌
- 播放列表管理

## 配置文件说明

项目使用 `config.json` 进行基础配置，包含以下四个字段：

- `GuildId`：Discord 服务器 ID
- `Channel`：服务器语音频道 ID
- `BotToken`：前往 https://discord.com/developers/applications 创建的机器人 Token
- `MusicApi`：网易云音乐 API 链接

示例：

```json
{
  "GuildId": "123456789012345678",
  "Channel": "123456789012345678",
  "BotToken": "your-bot-token",
  "MusicApi": "https://your-music-api.example.com"
}
```

## 部署

1. 下载 Release 中的文件
2. 配置好 `config.json`
3. 双击 `CloudMusicBot.exe` 启动

## 联系方式

- QQ群：541472742
- Discord：https://discord.gg/YeffUGYbas

## 支持项目

如果这个项目对你有帮助，欢迎点个 Star 和 Follow 支持一下！
