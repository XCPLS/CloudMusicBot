using System.Collections.Concurrent;
using Discord;

namespace CloudMusicBot.Interactions;

/// <summary>At most one open menu per user, dropped automatically after <see cref="Lifetime"/>.</summary>
public sealed class MenuManager
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<ulong, Entry> _open = new();

    public bool HasOpenMenu(ulong userId) => _open.ContainsKey(userId);

    public bool TryGet(ulong userId, out Menu menu)
    {
        if (_open.TryGetValue(userId, out var entry))
        {
            menu = entry.Menu;
            return true;
        }

        menu = null!;
        return false;
    }

    /// <summary>Opens a menu for the user, replacing whatever was open before.</summary>
    public Menu Open(
        ulong userId,
        string customId,
        string placeholder,
        bool multiSelect,
        IEnumerable<SelectMenuOptionBuilder> options)
    {
        var menu = new Menu(customId, placeholder, multiSelect, options);
        var expiry = new CancellationTokenSource();

        if (_open.TryRemove(userId, out var previous))
        {
            Cancel(previous.Expiry);
            previous.Expiry.Dispose();
        }

        _open[userId] = new Entry(menu, expiry);
        _ = ExpireAsync(userId, menu, expiry);
        return menu;
    }

    public void Close(ulong userId)
    {
        if (_open.TryRemove(userId, out var entry))
        {
            Cancel(entry.Expiry);
            entry.Expiry.Dispose();
        }
    }

    private async Task ExpireAsync(ulong userId, Menu menu, CancellationTokenSource expiry)
    {
        try
        {
            await Task.Delay(Lifetime, expiry.Token);
        }
        catch (OperationCanceledException)
        {
            // Replaced or closed by the user.
            return;
        }

        // Drops the entry only if it is still the one this timer was started for: a menu
        // opened between the check and the removal has to survive.
        if (_open.TryGetValue(userId, out var current) && ReferenceEquals(current.Menu, menu))
        {
            _open.TryRemove(new KeyValuePair<ulong, Entry>(userId, current));
            expiry.Dispose();
        }
    }

    private static void Cancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already closed.
        }
    }

    private sealed record Entry(Menu Menu, CancellationTokenSource Expiry);
}
