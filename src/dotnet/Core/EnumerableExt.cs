namespace ActualChat;

public static class EnumerableExt
{
    public static IEnumerable<T> Concat<T>(this IEnumerable<T> prefix, T suffix)
    {
        foreach (var item in prefix)
            yield return item;
        yield return suffix;
    }

    public static IEnumerable<T> PrefixWith<T>(this IEnumerable<T> source, T prefix)
    {
        yield return prefix;
        foreach (var item in source)
            yield return item;
    }

    public static int FirstIndexOf<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        var index = 0;
        foreach (var item in source) {
            if (predicate.Invoke(item))
                return index;

            index++;
        }
        return -1;
    }

    public static int LastIndexOf<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        var lastIndex = -1;
        var index = 0;
        foreach (var item in source) {
            if (predicate.Invoke(item))
                lastIndex = index;
            index++;
        }
        return lastIndex;
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
        return (matched?.ToArray() ?? [], notMatched?.ToArray() ?? []);
    }

    public static bool StartsWith<T>(this IEnumerable<T> left, IReadOnlyCollection<T> right)
        => left.Take(right.Count).SequenceEqual(right);

    public static bool SetEquals<T>(this IReadOnlySet<T> first, IReadOnlyCollection<T> second)
        => first.Count == second.Count && second.All(first.Contains);

    // Constructs "a, b, and c" strings
    public static string ToCommaPhrase(this IEnumerable<string> source, string op = "and")
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
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
            sb.Append(' ').Append(op).Append(' ');
            break;
        default:
            sb.Append(", ").Append(op).Append(' ');
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

    public static async Task<List<T>> Flatten<T>(this Task<ApiArray<ApiArray<T>>> task)
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
