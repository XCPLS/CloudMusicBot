using System.Collections.Concurrent;

namespace CloudMusicBot.Sessions;

/// <summary>
/// Phone numbers waiting for the sms code modal, keyed by Discord user.
/// Entries carry their own deadline so an abandoned login never keeps the
/// command handler alive.
/// </summary>
public sealed class CaptchaChallengeStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<ulong, Entry> _pending = new();

    public void Set(ulong userId, string phone)
    {
        _pending[userId] = new Entry(phone, DateTimeOffset.UtcNow + Lifetime);
        Sweep();
    }

    /// <summary>Reads and consumes the pending phone number.</summary>
    public bool TryTake(ulong userId, out string phone)
    {
        phone = "";
        if (!_pending.TryRemove(userId, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        phone = entry.Phone;
        return true;
    }

    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (userId, entry) in _pending)
        {
            if (entry.ExpiresAt <= now)
            {
                _pending.TryRemove(userId, out _);
            }
        }
    }

    private readonly record struct Entry(string Phone, DateTimeOffset ExpiresAt);
}
