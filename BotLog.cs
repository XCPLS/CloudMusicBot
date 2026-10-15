using Discord;

namespace CloudMusicBot;

/// <summary>Timestamps every line so logs from different tasks stay readable.</summary>
public static class BotLog
{
    private static readonly Lock Gate = new();

    public static void Write(LogSeverity severity, string message)
    {
        lock (Gate)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{severity}] {message}");
        }
    }

    public static Task HandleAsync(LogMessage log)
    {
        Write(log.Severity, log.Exception is null
            ? $"{log.Source}: {log.Message}"
            : $"{log.Source}: {log.Message}{Environment.NewLine}{log.Exception}");
        return Task.CompletedTask;
    }

    public static void Info(string message) => Write(LogSeverity.Info, message);

    public static void Warn(string message) => Write(LogSeverity.Warning, message);

    public static void Error(string message) => Write(LogSeverity.Error, message);
}
