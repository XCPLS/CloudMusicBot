namespace CloudMusicBot.Models;

public sealed record PlaylistSummary(ulong Id, string Name);

public sealed record UserSummary(ulong Id, string Nickname);

/// <summary>NetEase account behind a stored cookie.</summary>
public sealed record AccountInfo(ulong UserId, string Nickname);

/// <summary>Result of a successful login: the <c>MUSIC_U</c> value plus the account it belongs to.</summary>
public sealed record LoginResult(string Cookie, string Nickname, ulong UserId);

public enum QrLoginStatus
{
    Waiting,
    Scanned,
    Confirmed,
    Expired
}

public sealed record QrCheckResult(QrLoginStatus Status, string? Cookie = null);
