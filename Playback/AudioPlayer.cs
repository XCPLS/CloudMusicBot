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
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan VoiceConnectTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan VoiceDisconnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StreamWatchInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StatusUpdateTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Attempts per song, counting the first one; the rest are retries.</summary>
    private const int MaxAttempts = 3;

    private readonly Bot _bot;
    private readonly MusicApi _api;
    private readonly Lock _gate = new();
    private readonly Queue<Music> _queue = new();
    private readonly SemaphoreSlim _wakeup = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();

    private Task? _worker;

    // Written by the worker, read by the Disconnected callback on a gateway thread.
    private volatile IAudioClient? _voice;

    /// <summary>Set by the Disconnected callback, cleared when a fresh client is built.</summary>
    private volatile bool _voiceLost;

    /// <summary>Last status asked for, so an identical update is never sent twice.</summary>
    private volatile string _channelStatus = DefaultChannelStatus;

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

        string? audioFile = null;
        try
        {
            // Resolving the url stays outside the retry loops: a song that cannot be
            // played at all will not become playable by trying again.
            var streamUrl = await _api.GetSongStreamUrlAsync(music.Id, music.Owner, play.Token);

            // The whole song is buffered locally before playback starts. Handing ffmpeg
            // the network stream directly makes it stall every time the connection
            // hiccups, which is audible as stuttering, and containers that need to seek
            // (m4a/mp4) cannot be probed from a pipe at all.
            audioFile = await DownloadAsync(streamUrl, play.Token);

            await SendToVoiceChannelAsync(embed: Embeds.NowPlaying(music));
            RequestChannelStatus($"正在听 {music.Title}");

            await StreamWithReconnectAsync(audioFile, play.Token);
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
            _current = null;
            _currentPlay = null;
            DeleteAudioFile(audioFile);
        }
    }

    /// <summary>
    /// Downloads the song to a temp file. Retried on its own: a download that dies
    /// halfway has nothing to do with the voice connection.
    /// </summary>
    private async Task<string> DownloadAsync(string streamUrl, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await DownloadOnceAsync(streamUrl, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e) when (attempt < MaxAttempts && e is not BotApiException)
            {
                BotLog.Warn($"音频下载中断({e.Message})，正在重试，第 {attempt}/{MaxAttempts - 1} 次");
                await Task.Delay(RetryDelay, ct);
            }
        }
    }

    private async Task<string> DownloadOnceAsync(string streamUrl, CancellationToken ct)
    {
        using var response = await _api.OpenStreamAsync(streamUrl, ct);
        var path = BuildAudioPath(response, streamUrl);
        try
        {
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 16))
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            {
                await source.CopyToAsync(file, ct);
            }

            var actual = new FileInfo(path).Length;
            if (actual == 0)
            {
                throw new IOException("音频下载为空");
            }

            // A truncated body plays as a song that ends early, so treat it as a failure.
            if (response.Content.Headers.ContentLength is > 0 and var expected && actual < expected)
            {
                throw new IOException($"音频下载不完整({actual}/{expected})");
            }

            return path;
        }
        catch
        {
            DeleteAudioFile(path);
            throw;
        }
    }

    /// <summary>Temp file with an extension taken from the response, so ffmpeg probes less.</summary>
    private static string BuildAudioPath(HttpResponseMessage response, string streamUrl)
    {
        var extension = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() switch
        {
            "audio/mpeg" or "audio/mp3" => ".mp3",
            "audio/mp4" or "audio/m4a" or "audio/x-m4a" or "audio/aac" => ".m4a",
            "audio/flac" or "audio/x-flac" => ".flac",
            "audio/wav" or "audio/x-wav" => ".wav",
            "audio/ogg" => ".ogg",
            _ => Path.GetExtension(Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri) ? uri.AbsolutePath : "")
        };

        if (extension.Length is <= 1 or > 6 || !extension[1..].All(char.IsLetterOrDigit))
        {
            extension = ".mp3";
        }

        return Path.Combine(Path.GetTempPath(), $"cloudmusic-{Guid.NewGuid():N}{extension}");
    }

    private static void DeleteAudioFile(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
            BotLog.Warn($"删除音频缓存失败({path}): {e.Message}");
        }
    }

    /// <summary>
    /// Streams the song, rebuilding the voice connection when it drops instead of
    /// abandoning the song. Only the current song is retried, and only a few times.
    /// </summary>
    private async Task StreamWithReconnectAsync(string audioPath, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await StreamOnceAsync(audioPath, ct);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e) when (attempt < MaxAttempts && e is not BotApiException)
            {
                BotLog.Warn($"播放中断({e.Message})，{RetryDelay.TotalSeconds:0} 秒后重连，" +
                            $"第 {attempt}/{MaxAttempts - 1} 次");
                await DisconnectVoiceAsync();
                await Task.Delay(RetryDelay, ct);
            }
        }
    }

    private async Task StreamOnceAsync(string audioPath, CancellationToken ct)
    {
        if (_bot.VoiceChannel is not { } channel)
        {
            throw new BotApiException("语音频道不可用");
        }

        var voice = await EnsureVoiceAsync(channel);
        ct.ThrowIfCancellationRequested();

        // The pcm stream comes first: if the connection dies right here, no ffmpeg
        // process is left running behind us.
        await using var pcm = voice.CreatePCMStream(AudioApplication.Music);
        var ffmpeg = StartFfmpeg(audioPath);
        try
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var pump = ffmpeg.StandardOutput.BaseStream.CopyToAsync(pcm, lifetime.Token);
            await WaitForStreamAsync(pump, lifetime, ct);
        }
        finally
        {
            // ffmpeg has to be gone before the pcm stream is disposed below; a live
            // ffmpeg writing into a disposed stream is what used to hang playback.
            await StopFfmpegAsync(ffmpeg);
        }
    }

    /// <summary>
    /// Waits for the song to end, bailing out as soon as the voice connection is
    /// known to be gone: a write into a dropped socket does not reliably fail on its
    /// own, and the song would otherwise sit there silently consuming the queue.
    /// </summary>
    private async Task WaitForStreamAsync(Task streaming, CancellationTokenSource lifetime, CancellationToken ct)
    {
        while (true)
        {
            if (await Task.WhenAny(streaming, Task.Delay(StreamWatchInterval)) == streaming)
            {
                // Surfaces whatever the stream itself failed with.
                await streaming;
                return;
            }

            ct.ThrowIfCancellationRequested();

            if (_voiceLost)
            {
                await lifetime.CancelAsync();
                throw new IOException("语音连接已断开");
            }
        }
    }

    private Process StartFfmpeg(string audioPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _bot.Config.FfmpegPath,
            // -nostdin keeps ffmpeg from treating our console as an interaction terminal.
            Arguments = $"-hide_banner -loglevel error -nostdin -i \"{audioPath}\" -vn " +
                        "-acodec pcm_s16le -f s16le -ar 48000 -ac 2 pipe:1",
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
        // ConnectionState alone is not enough: it can still read Connected for a
        // moment after the socket is gone, which is why the callback sets _voiceLost.
        if (!_voiceLost && _voice is { ConnectionState: ConnectionState.Connected } connected)
        {
            return connected;
        }

        await DisconnectVoiceAsync();

        using var timeout = new CancellationTokenSource(VoiceConnectTimeout);
        var voice = await channel.ConnectAsync().WaitAsync(timeout.Token);
        voice.Disconnected += exception => OnVoiceDisconnected(voice, exception);
        _voice = voice;
        _voiceLost = false;
        return voice;
    }

    private Task OnVoiceDisconnected(IAudioClient source, Exception? exception)
    {
        // A late callback from a client we already replaced must not mark the new one dead.
        if (!ReferenceEquals(_voice, source))
        {
            return Task.CompletedTask;
        }

        _voiceLost = true;
        BotLog.Warn(exception is null ? "语音连接已断开" : $"语音连接断开: {exception.Message}");
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

        _voiceLost = false;

        if (voice is null)
        {
            return;
        }

        try
        {
            // StopAsync closes the voice websocket and leaves the channel. Bounded,
            // because a client that is already gone can otherwise leave the
            // playback worker waiting on it forever.
            using var timeout = new CancellationTokenSource(VoiceDisconnectTimeout);
            await voice.StopAsync().WaitAsync(timeout.Token);
        }
        catch (Exception e)
        {
            BotLog.Warn($"断开语音连接失败: {e.Message}");
        }
        finally
        {
            voice.Dispose();
        }

        RequestChannelStatus(DefaultChannelStatus);
    }

    /// <summary>
    /// The channel status is cosmetic, so it never runs on the playback path. Discord
    /// rate limits channel edits hard, and awaiting that call used to stall the queue
    /// for minutes once the limit was hit.
    /// </summary>
    private void RequestChannelStatus(string status)
    {
        if (_channelStatus == status)
        {
            return;
        }

        _channelStatus = status;
        _ = ApplyChannelStatusAsync(status);
    }

    private async Task ApplyChannelStatusAsync(string status)
    {
        try
        {
            if (_bot.VoiceChannel is { } channel)
            {
                using var timeout = new CancellationTokenSource(StatusUpdateTimeout);
                await channel.SetStatusAsync(status).WaitAsync(timeout.Token);
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
