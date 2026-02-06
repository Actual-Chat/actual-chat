namespace ActualChat.Diff.Handlers;

/// <summary>
/// Handles string diffs with null value encoding.
/// </summary>
public sealed class StringDiffHandler(DiffEngine engine) : DiffHandlerBase<string?, string?>(engine)
{
    private const char EscapeChar = '\x1b'; // ACSII ESC character, extremely unlikely to be the first one in any string
    private static readonly string EscapedNull = EscapeChar + "0";

    public override string? Diff(string? source, string? target)
        => OrdinalEquals(source, target) ? null : Escape(target);

    public override string? Patch(string? source, string? diff)
        => ReferenceEquals(diff, null) ? source : Unescape(diff);

    public static string? Escape(string? s)
    {
        if (s == null)
            return EscapedNull;

        if (s.Length == 0)
            return s;

        return s[0] == EscapeChar
            ? EscapeChar + s
            : s;
    }

    public static string? Unescape(string? s)
    {
        if (s.IsNullOrEmpty())
            return s;

        if (OrdinalEquals(s, EscapedNull))
            return null;

        return s[0] == EscapeChar
            ? s[1..]
            : s;
    }
}
