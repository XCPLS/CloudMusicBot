# CloudMusicBot

CloudMusicBot 是一个使用 C# 开发的 Discord 音乐机器人，在 Discord 语音房中播放网易云音乐。

歌曲先分段并发下载到本地临时文件，再由 ffmpeg 转码推流到语音频道；点歌、歌单、登录状态都通过斜杠命令和交互式表单完成。

## 功能介绍

- 二维码 / 密码 / 短信验证码三种方式登录网易云账号（多用户各自登录，互不影响）
- 搜索歌曲、按 ID 或分享链接点歌
- 搜索用户、浏览自己和他人歌单，分页勾选批量点歌
- 播放列表查看、跳过、退出播放

## 环境要求

| 依赖 | 说明 |
| --- | --- |
| .NET 10 | 运行或编译本项目 |
| ffmpeg | 转码音频，需在 PATH 中，或在配置里指定完整路径 |
| 网易云音乐 API 服务 | 本项目只负责调用，需自行部署，例如 [NeteaseCloudMusicApi](https://github.com/Binaryify/NeteaseCloudMusicApi) |

## 配置

首次启动会在程序目录生成 `config.json`：

```json
{
  "GuildId": 123456789012345678,
  "ChannelId": 123456789012345678,
  "BotToken": "your-bot-token",
  "MusicApi": "http://localhost:3000",
  "FfmpegPath": "ffmpeg",
  "ParallelDownload": true,
  "DownloadThreads": 4
}
```

| 字段 | 说明 |
| --- | --- |
| `GuildId` | Discord 服务器 ID |
| `ChannelId` | 语音频道 ID，机器人只会在这个频道播放 |
| `BotToken` | 在 https://discord.com/developers/applications 创建的机器人 Token |
| `MusicApi` | 网易云音乐 API 服务地址，末尾不要带 `/` |
| `FfmpegPath` | ffmpeg 可执行文件路径，已加入 PATH 时保持 `ffmpeg` 即可 |
| `ParallelDownload` | 是否多线程分段下载歌曲，默认 `true` |
| `DownloadThreads` | 分段下载的并发数，默认 `4`，取值 1-8 |

> 机器人只需要服务器和语音状态权限，不需要开启 Message Content、Server Members 等特权 intent。

下载相关说明：`ParallelDownload` 打开时，机器人会先用一个 1 字节的 Range 请求探测服务端是否支持分段，
支持就按 `DownloadThreads` 拆成多段并发拉取、再按偏移写入同一个文件；服务端忽略 Range、返回完整响应时
自动退回单线程，不会多花一次请求。缓存服务端不支持断点续传时会自动退化为原来的单线程下载。

### 运行前检查

- 机器人已邀请进 `GuildId` 对应的服务器，且有连接、发言权限
- 自己在语音频道内有权限，否则无法设置频道状态（不影响播放）
- API 服务可访问：`curl http://localhost:3000/search?keywords=test`

## 命令

| 命令 | 说明 |
| --- | --- |
| `/login qr` | 二维码登录 |
| `/login password` | 账号密码登录 |
| `/login captcha` | 手机号 + 短信验证码登录 |
| `/logout` | 退出登录 |
| `/play search` | 搜索歌曲，分页勾选后点播 |
| `/play id` | 按歌曲 ID 点播 |
| `/play url` | 按分享链接点播 |
| `/user search` | 搜索用户，查看其歌单 |
| `/user me` | 查看自己账号的歌单 |
| `/queue` | 查看当前播放和待播列表 |
| `/skip` | 跳过当前歌曲（点歌人或频道管理员） |
| `/exit` | 停止播放并退出语音频道（频道管理员） |

登录信息保存在 `UserCookie.txt`，与 `config.json` 一样由程序自行读写，请勿提交到仓库。

## 部署

### 方式一：源码运行

```bash
dotnet run -c Release
```

### 方式二：发布后运行

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

把 `bin/Release/net10.0/win-x64/publish/` 下的文件放到任意目录，配置好 `config.json` 后双击 `CloudMusicBot.exe` 启动。

队列为空 5 分钟后机器人会自动退出语音频道，再次点歌时会自动重新连接。

播放过程中语音连接断开（网络抖动、Discord 重启等）时，机器人会自动重连并从当前这首歌重新开始播，连续 3 次失败才会放弃并提示。

## 常见问题

**启动后立刻退出**
日志里会写明原因，通常是 `config.json` 未填写完整，或机器人不在配置的服务器 / 语音频道里。

**点歌失败，提示歌曲不可播放**
多为版权或会员限制。播放使用的是**点歌人自己**的账号，请先 `/login` 登录一个有该歌曲播放权限的账号。

**没有声音 / 播放直接结束**
确认 ffmpeg 可用：命令行执行 `ffmpeg -version`，失败则在配置里填写 `FfmpegPath` 的完整路径。

**登录后仍提示需要登录**
`UserCookie.txt` 中的凭证已过期，重新 `/login` 一次即可。

## 联系方式

- QQ群：541472742
- Discord：https://discord.gg/YeffUGYbas

## 支持项目

如果这个项目对你有帮助，欢迎点个 Star 和 Follow 支持一下！
