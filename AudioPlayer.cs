using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using Discord;
using Discord.Audio;
using Newtonsoft.Json.Linq;

namespace CloudMusicBot;

public static class AudioPlayer
{
    public static readonly List<Music> PlayList = new List<Music>();
    public static Music? PlayingMusic { get; private set; }
    
    private static CancellationTokenSource? _cancellation;
    private static Process? _ffmpeg;

    private static readonly HttpClient HttpClient = new HttpClient();

    private static int _exitTimer = 0;

    internal static void Init()
    {
        _ = PlayListCheck();
    }

    private static async Task PlayListCheck()
    {
        IAudioClient? audioClient = null;
        while (true)
        {
            if (PlayList.Count != 0 && Program.MusicChannel != null)
            {
                _exitTimer = 0;
                _cancellation = new CancellationTokenSource();
                
                PlayingMusic = PlayList[0];
                PlayList.RemoveAt(0);
                try
                {
                    if (audioClient == null || audioClient.ConnectionState != ConnectionState.Connected)
                    {
                        audioClient = await Program.MusicChannel.ConnectAsync();
                        while (audioClient.ConnectionState != ConnectionState.Connected)
                        {
                            await Task.Delay(1000);
                        }

                        await Task.Delay(1000);
                    }

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.Color = Color.Green;
                    builder.WithCurrentTimestamp();
                    builder.WithFooter("CloudMusic");
                    builder.Title = "🎵正在播放";
                    builder.ThumbnailUrl = PlayingMusic.Cover;
                    builder.Description = $"歌曲: {PlayingMusic.Name}\n歌手: {PlayingMusic.Artist}\n专辑: 《{PlayingMusic.Album}》";
                    
                    await Program.MusicChannel.SendMessageAsync(embed: builder.Build());

                    await Program.MusicChannel.SetStatusAsync($"正在听 {PlayingMusic.Name} - {PlayingMusic.Artist}");
                    
                    var response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/song/url?id={PlayingMusic.Id}", PlayingMusic.Owner);
                    JObject data = (JObject) ((JArray)JObject.Parse(await response.Content.ReadAsStringAsync())["data"])[0];

                    if ((int) data["code"] != 200)
                    {
                        response = await HttpClient.GetAsyncWithTimestamp($"{Program.Config.MusicApi}/check/music?id={PlayingMusic.Id}", PlayingMusic.Owner);
                        data = JObject.Parse(await response.Content.ReadAsStringAsync());
                        throw new Exception((string)data["message"]);
                    }

                    string url = (string)data["url"];
                    
                    await using var discord = audioClient.CreatePCMStream(AudioApplication.Music);
                    _ffmpeg = Process.Start(new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-hide_banner -loglevel error -i \"{url}\" -acodec pcm_s16le -f s16le -ar 48000 -ac 2 -",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (_ffmpeg == null)
                        throw new Exception("ffmpeg 启动失败");
                    
                    await _ffmpeg.StandardOutput.BaseStream.CopyToAsync(discord, _cancellation.Token);
                    await _ffmpeg.WaitForExitAsync();
                    _ffmpeg = null;
                }
                catch (OperationCanceledException) {}
                catch (Exception e)
                {
                    Program.LogMessage(LogSeverity.Info, e.ToString());
                    EmbedBuilder builder = new EmbedBuilder();
                    builder.Color = Color.Red;
                    builder.WithCurrentTimestamp();
                    builder.WithFooter("CloudMusic");
                    builder.Title = "❌播放失败";
                    builder.Description = $"歌曲: {PlayingMusic.Name}\n歌手: {PlayingMusic.Artist}\n专辑: 《{PlayingMusic.Album}》\n信息: {e.Message}";
                    builder.ThumbnailUrl = PlayingMusic.Cover;
                    
                    await Program.MusicChannel.SendMessageAsync(embed: builder.Build());
                }
                PlayingMusic = null;
                _cancellation = null;
                await Program.MusicChannel.SetStatusAsync($"网易云音乐频道");
            }
            else if (audioClient != null && audioClient.ConnectionState == ConnectionState.Connected)
            {
                _exitTimer++;
                if (_exitTimer == 300)
                {
                    await Program.MusicChannel.DisconnectAsync();
                    audioClient = null;
                    _exitTimer = 0;
                }
            }
            await Task.Delay(1000);
        }
    }

    public static void StopPlay()
    {
        _cancellation?.Cancel();
        if (_ffmpeg != null && !_ffmpeg.HasExited)
        {
            _ffmpeg.Kill(true);
        }
    }
    
    public static void Exit()
    {
        PlayList.Clear();
        _cancellation?.Cancel();
        if (_ffmpeg != null && !_ffmpeg.HasExited)
        {
            _ffmpeg.Kill(true);
        }
        _ = Program.MusicChannel.DisconnectAsync();
        _exitTimer = 0;
    }
}