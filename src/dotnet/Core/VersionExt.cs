namespace ActualChat;

/// <summary>
/// Parses the version strings this app deals with - nbgv informational versions
/// (<c>2.17.246+sha</c>), assembly versions (<c>2.17.0.0</c>), display versions
/// (<c>v2.17.246 sha</c>) and store versions - into a comparable 3-component
/// <see cref="Version"/>.
/// </summary>
public static class VersionExt
{
    public static readonly Version Zero = new(0, 0, 0);
    private static readonly char[] TailSeparators = [' ', '+', '-'];

    public static Version ParseBuildVersion(string? version)
        => TryParseBuildVersion(version, out var result) ? result : Zero;

    public static bool TryParseBuildVersion(string? version, [MaybeNullWhen(false)] out Version result)
    {
        result = null;
        if (version.IsNullOrEmpty())
            return false;

        var s = version.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];
        var tailStart = s.IndexOfAny(TailSeparators);
        if (tailStart >= 0)
            s = s[..tailStart];

        var parts = s.Split('.');
        if (parts.Length > 4)
            return false;

        Span<int> numbers = [0, 0, 0];
        for (var i = 0; i < parts.Length; i++) {
            if (!int.TryParse(parts[i], out var number) || number < 0)
                return false;

            if (i < numbers.Length)
                numbers[i] = number;
        }
        result = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }
}
