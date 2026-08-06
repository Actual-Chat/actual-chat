namespace ActualChat.Kvas;

/// <summary>
/// Hidden KVAS keys: the server reads and writes them via the backend KVAS,
/// while the session-scoped APIs reachable by clients hide them on read and reject them on write.
/// Use them for per-user state the server trusts, e.g. access grants.
/// </summary>
public static class KvasKeys
{
    public const string HiddenPrefix = "@@";

    public static bool IsHidden(string key)
        => key.StartsWith(HiddenPrefix);

    public static void RequireNotHidden(string key)
    {
        if (IsHidden(key))
            throw StandardError.Constraint($"'{key}' is a hidden key.");
    }
}
