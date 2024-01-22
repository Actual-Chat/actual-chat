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

    public static (IReadOnlyCollection<T> Matched, IReadOnlyCollection<T> NotMatched) Split<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        List<T>? matched = null;
        List<T>? notMatched = null;
        foreach (var item in source)
            if (predicate(item)) {
                matched ??= new List<T>();
                matched.Add(item);
            }
            else {
                notMatched ??= new List<T>();
                notMatched.Add(item);
            }
        return (matched?.ToArray() ?? Array.Empty<T>(), notMatched?.ToArray() ?? Array.Empty<T>());
    }

    public static bool StartsWith<T>(this IEnumerable<T> left, IReadOnlyCollection<T> right)
        => left.Take(right.Count).SequenceEqual(right);

    public static IEnumerable<T> SkipNullItems<T>(this IEnumerable<T?> source)
        where T : class
        => source.Where(x => x != null)!;

    public static IEnumerable<T> SkipNullItems<T>(this IEnumerable<T?> source)
        where T : struct
        => source.Where(x => x != null)!.Select(x => x!.Value);

    public static IEnumerable<T> NoNullItems<T>(this IEnumerable<T?> source)
        where T : class
        => source!;

    public static bool SetEquals<T>(this IReadOnlySet<T> first, IReadOnlyCollection<T> second)
        => first.Count == second.Count && second.All(first.Contains);
    public static bool SmallSetEquals<T>(this IReadOnlyCollection<T> first, IReadOnlyCollection<T> second)
        => first.Count == second.Count && second.All(first.Contains);

    // Constructs "a, b, and c" strings
    public static string ToCommaPhrase(this IEnumerable<string> source)
    {
        var sb = StringBuilderExt.Acquire();
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
        return sb.ToStringAndRelease();
    }

    public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source)
        => source.Shuffle(Random.Shared);

    public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source, Random random)
        => source.ShuffleIterator(random);

    public static IOrderedEnumerable<T> ToFakeOrderedEnumerable<T>(this IEnumerable<T> source)
        => new FakeOrderedEnumerable<T>(source);

    public static async Task<List<T>> Flatten<T>(this Task<ApiArray<T>[]> task)
    {
        var arrays = await task.ConfigureAwait(false);
        return arrays.SelectMany(x => x).ToList();
    }

    private static IEnumerable<T> ShuffleIterator<T>(this IEnumerable<T> source, Random random)
    {
        var buffer = source.ToList();
        for (var i = 0; i < buffer.Count; i++) {
            int j = random.Next(i, buffer.Count);
            yield return buffer[j];

            buffer[j] = buffer[i];
        }
    }

    private class FakeOrderedEnumerable<T>(IEnumerable<T> source) : IOrderedEnumerable<T>
    {
        public IOrderedEnumerable<T> CreateOrderedEnumerable<TKey>(
            Func<T, TKey> keySelector,
            IComparer<TKey>? comparer,
            bool descending)
            => descending
                ? source.OrderByDescending(keySelector, comparer)
                : source.OrderBy(keySelector, comparer);

        public IEnumerator<T> GetEnumerator()
            => source.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
