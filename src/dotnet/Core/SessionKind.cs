namespace ActualChat;

public enum SessionKind
{
    Session = 0,
    ApiKey,
}

public static class SessionKindExt
{
    public static string ToReadable(this SessionKind kind)
        => kind switch {
            SessionKind.Session => "Session",
            SessionKind.ApiKey => "API Key",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
