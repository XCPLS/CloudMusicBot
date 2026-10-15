namespace CloudMusicBot;

public static class Program
{
    public static async Task Main()
    {
        var config = ConfigStore.Load();
        if (!config.TryValidate(out var error))
        {
            BotLog.Warn($"配置无效({error})，请编辑 config.json 后重新启动。");
            WaitForExit();
            return;
        }

        try
        {
            await using var bot = new Bot(config);
            await bot.RunAsync();
        }
        catch (Exception e)
        {
            BotLog.Error($"启动失败: {e.Message}");
            WaitForExit();
        }
    }

    /// <summary>Keeps the window open so a double-clicked exe does not swallow the reason it stopped.</summary>
    private static void WaitForExit()
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        Console.WriteLine("按任意键退出...");
        try
        {
            Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            // No console attached.
        }
    }
}
