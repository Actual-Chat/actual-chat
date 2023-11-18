using Microsoft.Extensions.Primitives;

namespace ActualChat;

public static class OrdinalStringExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEquals(string? x, string? y)
        => x?.Equals(y, StringComparison.Ordinal) ?? y is null;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEquals(Symbol x, string y)
        => x.Value.Equals(y, StringComparison.Ordinal);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEquals(string x, Symbol y)
        => y.Value.Equals(x, StringComparison.Ordinal);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEquals(Symbol x, Symbol y)
        => x.Value.Equals(y.Value, StringComparison.Ordinal);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEquals(StringSegment x, string y)
        => x.Equals(new StringSegment(y), StringComparison.Ordinal);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEquals(string x, StringSegment y)
        => y.Equals(new StringSegment(x), StringComparison.Ordinal);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEquals(StringSegment x, StringSegment y)
        => x.Equals(y, StringComparison.Ordinal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalIgnoreCaseEquals(string? x, string? y)
        => x?.Equals(y, StringComparison.OrdinalIgnoreCase) ?? y is null;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalIgnoreCaseEquals(Symbol x, string y)
        => x.Value.Equals(y, StringComparison.OrdinalIgnoreCase);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalIgnoreCaseEquals(string x, Symbol y)
        => y.Value.Equals(x, StringComparison.OrdinalIgnoreCase);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalIgnoreCaseEquals(StringSegment x, string y)
        => x.Equals(new StringSegment(y), StringComparison.OrdinalIgnoreCase);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalIgnoreCaseEquals(string x, StringSegment y)
        => y.Equals(new StringSegment(x), StringComparison.OrdinalIgnoreCase);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalIgnoreCaseEquals(StringSegment x, StringSegment y)
        => x.Equals(y, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalCompare(string? x, string? y)
        => StringComparer.Ordinal.Compare(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalCompare(StringSegment x, string? y)
        => StringSegment.Compare(x, new StringSegment(y), StringComparison.Ordinal);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalCompare(string? x, StringSegment y)
        => StringSegment.Compare(new StringSegment(x), y, StringComparison.Ordinal);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalCompare(StringSegment x, StringSegment y)
        => StringSegment.Compare(x, y, StringComparison.Ordinal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIgnoreCaseCompare(string? x, string? y)
        => StringComparer.OrdinalIgnoreCase.Compare(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIgnoreCaseCompare(StringSegment x, string? y)
        => StringSegment.Compare(x, new StringSegment(y), StringComparison.OrdinalIgnoreCase);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIgnoreCaseCompare(string? x, StringSegment y)
        => StringSegment.Compare(new StringSegment(x), y, StringComparison.OrdinalIgnoreCase);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIgnoreCaseCompare(StringSegment x, StringSegment y)
        => StringSegment.Compare(x, y, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalHashCode(this string? x)
        => x?.GetHashCode(StringComparison.Ordinal) ?? 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIgnoreCaseHashCode(this string? x)
        => x?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIndexOf(this string? x, string prefix)
        => x?.IndexOf(prefix, StringComparison.Ordinal) ?? -1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIndexOf(this string? x, string prefix, int startIndex)
        => x?.IndexOf(prefix, startIndex, StringComparison.Ordinal) ?? -1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIndexOf(this string? x, char prefix)
        => x?.IndexOf(prefix, StringComparison.Ordinal) ?? -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIgnoreCaseIndexOf(this string? x, string prefix)
        => x?.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) ?? -1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIgnoreCaseIndexOf(this string? x, string prefix, int startIndex)
        => x?.IndexOf(prefix, startIndex, StringComparison.OrdinalIgnoreCase) ?? -1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIgnoreCaseIndexOf(this string? x, char prefix)
        => x?.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) ?? -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalLastIndexOf(this string? x, string prefix)
        => x?.LastIndexOf(prefix, StringComparison.Ordinal) ?? -1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalLastIndexOf(this string? x, string prefix, int startIndex)
        => x?.LastIndexOf(prefix, startIndex, StringComparison.Ordinal) ?? -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIgnoreCaseLastIndexOf(this string? x, string prefix)
        => x?.LastIndexOf(prefix, StringComparison.OrdinalIgnoreCase) ?? -1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIgnoreCaseLastIndexOf(this string? x, string prefix, int startIndex)
        => x?.LastIndexOf(prefix, startIndex, StringComparison.OrdinalIgnoreCase) ?? -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalStartsWith(this string? x, string prefix)
        => x?.StartsWith(prefix, StringComparison.Ordinal) ?? false;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalStartsWith(this StringSegment x, string prefix)
        => x.StartsWith(prefix, StringComparison.Ordinal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalIgnoreCaseStartsWith(this string? x, string prefix)
        => x?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ?? false;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalIgnoreCaseStartsWith(this StringSegment x, string prefix)
        => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEndsWith(this string? x, string suffix)
        => x?.EndsWith(suffix, StringComparison.Ordinal) ?? false;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEndsWith(this StringSegment x, string suffix)
        => x.EndsWith(suffix, StringComparison.Ordinal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalIgnoreCaseEndsWith(this string? x, string suffix)
        => x?.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ?? false;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalIgnoreCaseEndsWith(this StringSegment x, string suffix)
        => x.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalContains(this string? x, string fragment)
        => x?.Contains(fragment, StringComparison.Ordinal) ?? false;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalContains(this string? x, char fragment)
        => x?.Contains(fragment, StringComparison.Ordinal) ?? false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalIgnoreCaseContains(this string? x, string fragment)
        => x?.Contains(fragment, StringComparison.OrdinalIgnoreCase) ?? false;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalIgnoreCaseContains(this string? x, char fragment)
        => x?.Contains(fragment, StringComparison.OrdinalIgnoreCase) ?? false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string OrdinalReplace(this string x, string oldValue, string newValue)
        => x.Replace(oldValue, newValue, StringComparison.Ordinal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string OrdinalIgnoreCaseReplace(this string x, string oldValue, string newValue)
        => x.Replace(oldValue, newValue, StringComparison.OrdinalIgnoreCase);

    public static bool OrdinalHasPrefix(this string source, string prefix, out string suffix)
        => source.HasPrefix(prefix, StringComparison.Ordinal, out suffix);
    public static bool OrdinalIgnoreCaseHasPrefix(this string source, string prefix, out string suffix)
        => source.HasPrefix(prefix, StringComparison.OrdinalIgnoreCase, out suffix);
    public static bool HasPrefix(this string source, string prefix, StringComparison stringComparison, out string suffix)
    {
        if (source.StartsWith(prefix, stringComparison)) {
            suffix = source[prefix.Length..];
            return true;
        }
        suffix = "";
        return false;
    }
}
