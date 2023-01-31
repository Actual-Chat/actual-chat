using Cysharp.Text;

namespace ActualChat;

public static class EnumerableExt
{
    public static IEnumerable<T> Concat<T>(this IEnumerable<T> prefix, T suffix)
    {
        foreach (var item in prefix)
            yield return item;
        yield return suffix;
    }

    public static bool StartsWith<T>(this IEnumerable<T> left, IReadOnlyCollection<T> right)
        => left.Take(right.Count).SequenceEqual(right);

    public static IEnumerable<T> SkipNullItems<T>(this IEnumerable<T?> source)
        where T : class
        => source.Where(x => x != null)!;

    public static IEnumerable<T> NoNullItems<T>(this IEnumerable<T?> source)
        where T : class
        => source!;

    // Constructs "a, b, and c" strings
    public static string ToCommaPhrase(this IEnumerable<string> source)
    {
        using var sb = ZString.CreateStringBuilder();
        var prev = "";
        var i = 0;
        foreach (var item in source) {
            if (i >= 2) // Since we append prev, i=0 is always empty and i=1 is very first item
                sb.Append(", ");
            sb.Append(prev);
            prev = item;
            i++;
        }

        switch (i) { // i is total item count here
        case 0:
            break;
        case 1:
            break;
        case 2:
            sb.Append(" and ");
            break;
        default:
            sb.Append(", and ");
            break;
        }
        sb.Append(prev);
        return sb.ToString();
    }
}
