using Microsoft.Extensions.Primitives;

namespace ActualChat;

public static class OrdinalStringExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEquals(string? x, string? y)
        => x?.Equals(y) ?? y is null;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEquals(Symbol x, string y)
        => x.Value.Equals(y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEquals(string x, Symbol y)
        => y.Value.Equals(x);
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
    public static int OrdinalHashCode(this string? x)
        => x?.GetHashCode(StringComparison.Ordinal) ?? 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalIndexOf(this string? x, string prefix)
        => x?.IndexOf(prefix, StringComparison.Ordinal) ?? -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrdinalLastIndexOf(this string? x, string prefix)
        => x?.LastIndexOf(prefix, StringComparison.Ordinal) ?? -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalStartsWith(this string? x, string prefix)
        => x?.StartsWith(prefix, StringComparison.Ordinal) ?? false;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalStartsWith(this StringSegment x, string prefix)
        => x.StartsWith(prefix, StringComparison.Ordinal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEndsWith(this string? x, string suffix)
        => x?.EndsWith(suffix, StringComparison.Ordinal) ?? false;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalEndsWith(this StringSegment x, string suffix)
        => x.EndsWith(suffix, StringComparison.Ordinal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalContains(this string? x, string fragment)
        => x?.Contains(fragment, StringComparison.Ordinal) ?? false;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OrdinalContains(this string? x, char fragment)
        => x?.Contains(fragment, StringComparison.Ordinal) ?? false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string OrdinalReplace(this string x, string oldValue, string newValue)
        => x.Replace(oldValue, newValue, StringComparison.Ordinal);
}
