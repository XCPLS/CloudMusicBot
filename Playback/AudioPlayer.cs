using System.Diagnostics;
using Discord;
using Discord.Audio;
using Discord.WebSocket;

namespace CloudMusicBot.Playback;

/// <summary>
/// Single background worker that drains the queue one song at a time,
/// pipes the audio through ffmpeg and into the voice channel.
/// </summary>
public sealed class AudioPlayer : IAsyncDisposable
{
    private const string DefaultChannelStatus = "网易云音乐";

    private static readonly TimeSpan IdleDisconnectDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FfmpegArgumentWait = TimeSpan.FromSeconds(5);

    private readonly Bot _bot;
    private readonly MusicApi _api;
    private readonly Lock _gate = new();
    private readonly Queue<Music> _queue = new();
    private readonly SemaphoreSlim _wakeup = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();

    private Task? _worker;
    private IAudioClient? _voice;

    // Read from gateway threads (queue/skip) while the worker writes them.
    private volatile Music? _current;
    private volatile CancellationTokenSource? _currentPlay;
    private volatile bool _exitRequested;

    public AudioPlayer(Bot bot)
    {
        _bot = bot;
        _api = bot.Api;
    }

    public Music? Current => _current;

    public bool IsPlaying => _current is not null;

    public Music[] QueueSnapshot()
    {
        lock (_gate)
        {
            return _queue.ToArray();
        }
    }

    public void Enqueue(Music music)
    {
        lock (_gate)
        {
            _queue.Enqueue(music);
        }

        Signal();
        EnsureWorker();
    }

    public void EnqueueRange(IEnumerable<Music> musics)
    {
        lock (_gate)
        {
            foreach (var music in musics)
            {
                _queue.Enqueue(music);
            }
        }

        Signal();
        EnsureWorker();
    }

    /// <summary>Skips the song that is playing right now.</summary>
    public bool Skip()
    {
        var play = _currentPlay;
        if (play is null || _current is null)
        {
            return false;
        }

        try
        {
            play.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The song finished between the null check and the cancel.
            return false;
        }
    }

    /// <summary>Stops playback, drops the queue and leaves the voice channel.</summary>
    public void Exit()
    {
        _exitRequested = true;
        Skip();
        Signal();
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        Signal();

        if (_worker is { } worker)
        {
            try
            {
                await worker.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception)
            {
                // Shutting down anyway.
            }
        }

        await DisconnectVoiceAsync();
        _wakeup.Dispose();
        _shutdown.Dispose();
    }

    private void Signal()
    {
        if (_wakeup.CurrentCount > 0)
        {
            return;
        }

        try
        {
            _wakeup.Release();
        }
        catch (SemaphoreFullException)
        {
            // Another thread signalled first; the worker will still wake up.
        }
    }

    private void EnsureWorker()
    {
        lock (_gate)
        {
            if (_worker is { IsCompleted: false })
            {
                return;
            }

            _worker = Task.Run(() => RunAsync(_shutdown.Token));
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_exitRequested)
                {
                    _exitRequested = false;
                    lock (_gate)
                    {
                        _queue.Clear();
                    }

                    await DisconnectVoiceAsync();
                    continue;
                }

                if (TryDequeue(out var music))
                {
                    await PlayAsync(music!, ct);
                    continue;
                }

                // Nothing queued: leave the channel after a while, or wait for the next song.
                if (!await WaitForWorkAsync(ct))
                {
                    await DisconnectVoiceAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Bot is shutting down.
        }
        catch (Exception e)
        {
            BotLog.Error($"播放任务异常退出: {e}");
        }
    }

    private async Task<bool> WaitForWorkAsync(CancellationToken ct)
    {
        var timeout = _voice is null ? Timeout.InfiniteTimeSpan : IdleDisconnectDelay;
        return await _wakeup.WaitAsync(timeout, ct);
    }

    private bool TryDequeue(out Music? music)
    {
        lock (_gate)
        {
            if (_queue.Count > 0)
            {
                music = _queue.Dequeue();
                return true;
            }
        }

        music = null;
        return false;
    }

    private async Task PlayAsync(Music music, CancellationToken ct)
    {
        using var play = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _currentPlay = play;
        _current = music;

        Process? ffmpeg = null;
        try
        {
            if (_bot.VoiceChannel is not { } channel)
            {
                throw new BotApiException("语音频道不可用");
            }

            var voice = await EnsureVoiceAsync(channel);
            var streamUrl = await _api.GetSongStreamUrlAsync(music.Id, music.Owner, play.Token);
            using var response = await _api.OpenStreamAsync(streamUrl, play.Token);
            play.Token.ThrowIfCancellationRequested();

            await SendToVoiceChannelAsync(embed: Embeds.NowPlaying(music));
            await SetChannelStatusAsync($"正在听 {music.Title}");

            ffmpeg = StartFfmpeg();
            await using var pcm = voice.CreatePCMStream(AudioApplication.Music);
            try
            {
                await using var source = await response.Content.ReadAsStreamAsync(play.Token);
                var feed = FeedAsync(source, ffmpeg.StandardInput.BaseStream, play.Token);
                var pump = ffmpeg.StandardOutput.BaseStream.CopyToAsync(pcm, play.Token);
                await Task.WhenAll(feed, pump);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                // ffmpeg closed the pipe, usually because the song ended.
            }
            finally
            {
                // ffmpeg has to be gone before the pcm stream closes, otherwise the
                // discord stream can block on its own buffer.
                await StopFfmpegAsync(ffmpeg);
                ffmpeg = null;
            }
        }
        catch (OperationCanceledException)
        {
            // Skipped or shutting down.
        }
        catch (Exception e)
        {
            BotLog.Error($"播放失败({music.DisplayName}): {e.Message}");
            await SendToVoiceChannelAsync(embed: Embeds.PlayFailed(music, e.Message));
        }
        finally
        {
            if (ffmpeg is not null)
            {
                await StopFfmpegAsync(ffmpeg);
            }

            _current = null;
            _currentPlay = null;
            await SetChannelStatusAsync(DefaultChannelStatus);
        }
    }

    /// <summary>Streams the http body into ffmpeg, then closes its input to signal the end of the song.</summary>
    private static async Task FeedAsync(Stream source, Stream destination, CancellationToken ct)
    {
        try
        {
            await source.CopyToAsync(destination, ct);
            await destination.FlushAsync(ct);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // ffmpeg is gone; nothing left to feed.
        }
        finally
        {
            try
            {
                destination.Close();
            }
            catch (Exception)
            {
                // Already closed.
            }
        }
    }

    private Process StartFfmpeg()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _bot.Config.FfmpegPath,
            Arguments = "-hide_banner -loglevel error -i pipe:0 -vn -acodec pcm_s16le -f s16le -ar 48000 -ac 2 pipe:1",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException();
        }
        catch (Exception e)
        {
            throw new BotApiException($"ffmpeg 启动失败({_bot.Config.FfmpegPath})", e);
        }

        // Drain stderr so a chatty ffmpeg can never block on a full pipe.
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                BotLog.Warn($"ffmpeg: {args.Data}");
            }
        };
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task StopFfmpegAsync(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            using var timeout = new CancellationTokenSource(FfmpegArgumentWait);
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (Exception)
        {
            // Already gone, or it refused to die within the grace period.
        }
        finally
        {
            process.Dispose();
        }
    }

    private async Task<IAudioClient> EnsureVoiceAsync(SocketVoiceChannel channel)
    {
        if (_voice is { ConnectionState: ConnectionState.Connected } connected)
        {
            return connected;
        }

        await DisconnectVoiceAsync();

        var voice = await channel.ConnectAsync();
        voice.Disconnected += OnVoiceDisconnected;
        _voice = voice;
        return voice;
    }

    private Task OnVoiceDisconnected(Exception? exception)
    {
        if (exception is not null)
        {
            BotLog.Warn($"语音连接断开: {exception.Message}");
        }

        return Task.CompletedTask;
    }

    private async Task DisconnectVoiceAsync()
    {
        IAudioClient? voice;
        lock (_gate)
        {
            voice = _voice;
            _voice = null;
        }

        if (voice is null)
        {
            return;
        }

        try
        {
            voice.Disconnected -= OnVoiceDisconnected;
            // StopAsync closes the voice websocket and leaves the channel.
            await voice.StopAsync();
            voice.Dispose();
        }
        catch (Exception e)
        {
            BotLog.Warn($"断开语音连接失败: {e.Message}");
        }

        await SetChannelStatusAsync(DefaultChannelStatus);
    }

    private async Task SetChannelStatusAsync(string status)
    {
        try
        {
            if (_bot.VoiceChannel is { } channel)
            {
                await channel.SetStatusAsync(status);
            }
        }
        catch (Exception e)
        {
            BotLog.Warn($"设置频道状态失败: {e.Message}");
        }
    }

    private async Task SendToVoiceChannelAsync(Embed embed)
    {
        try
        {
            if (_bot.VoiceChannel is { } channel)
            {
                await channel.SendMessageAsync(embed: embed);
            }
        }
        catch (Exception e)
        {
            BotLog.Warn($"发送播放消息失败: {e.Message}");
        }
    }
}
