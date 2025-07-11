namespace ActualChat;

public static class EnumerableExt
{
    // TODO: Remove this workaround when LINQ .Reverse for arrays stops overlapping with System.MemoryExtensions
    public static IEnumerable<T> Revert<T>(this IEnumerable<T> source)
        => source.Reverse();

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

    public static IEnumerable<(T? Left, T? Right)> Merge<T>(
        this IEnumerable<T> left,
        IEnumerable<T> right)
        => left.Merge(right, x => x, x => x);

    public static IEnumerable<(T? Left, T? Right)> Merge<T>(
        this IEnumerable<T> left,
        IEnumerable<T> right,
        Comparison<T> keyComparer)
        => left.Merge(right, x => x, x => x, Comparer<T>.Create(keyComparer));

    public static IEnumerable<(T? Left, T? Right)> Merge<T>(
        this IEnumerable<T> left,
        IEnumerable<T> right,
        IComparer<T> keyComparer)
        => left.Merge(right, x => x, x => x, keyComparer);

    public static IEnumerable<(TLeft? Left, TRight? Right)> Merge<TLeft, TRight, TKey>(
        this IEnumerable<TLeft> left,
        IEnumerable<TRight> right,
        Func<TLeft, TKey> leftKeySelector,
        Func<TRight, TKey> rightKeySelector,
        Comparison<TKey> keyComparer)
        => left.Merge(right, leftKeySelector, rightKeySelector, Comparer<TKey>.Create(keyComparer));

    /// <summary>
    /// Performs a full outer join between two ordered sequences based on matching keys.
    /// </summary>
    /// <typeparam name="TLeft">The type of elements in the left sequence.</typeparam>
    /// <typeparam name="TRight">The type of elements in the right sequence.</typeparam>
    /// <typeparam name="TKey">The type of keys used for matching elements.</typeparam>
    /// <param name="left">The left sequence to join. Must be ordered by the key returned by <paramref name="leftKeySelector"/>.</param>
    /// <param name="right">The right sequence to join. Must be ordered by the key returned by <paramref name="rightKeySelector"/>.</param>
    /// <param name="leftKeySelector">A function to extract the join key from each element of the left sequence.</param>
    /// <param name="rightKeySelector">A function to extract the join key from each element of the right sequence.</param>
    /// <param name="keyComparer">An optional comparer for the keys. If null, the default comparer for <typeparamref name="TKey"/> is used.</param>
    /// <returns>
    /// A sequence of tuples where:
    /// - Matching elements are paired together (all possible combinations if multiple elements have the same key)
    /// - Elements with no match are paired with <see langword="default"/> on the other side
    /// </returns>
    /// <remarks>
    /// This method assumes both input sequences are already ordered by their respective keys.
    /// For efficiency, it performs a single pass through both sequences, matching elements as it goes.
    /// The method handles cases where multiple elements in each sequence have the same key by creating
    /// all possible combinations of matches (Cartesian product).
    /// </remarks>
    public static IEnumerable<(TLeft? Left, TRight? Right)> Merge<TLeft, TRight, TKey>(
        this IEnumerable<TLeft> left,
        IEnumerable<TRight> right,
        Func<TLeft, TKey> leftKeySelector,
        Func<TRight, TKey> rightKeySelector,
        IComparer<TKey>? keyComparer = null)
    {
        keyComparer ??= Comparer<TKey>.Default;
        using var leftEnumerator = left.GetEnumerator();
        using var rightEnumerator = right.GetEnumerator();

        var leftHasNext = leftEnumerator.MoveNext();
        var rightHasNext = rightEnumerator.MoveNext();

        // Keep track of items that are matching
        var leftMatches = new List<TLeft>(4);
        var rightMatches = new List<TRight>(4);

        while (leftHasNext && rightHasNext) {
            var leftKey = leftKeySelector(leftEnumerator.Current);
            var rightKey = rightKeySelector(rightEnumerator.Current);
            var comparison = keyComparer.Compare(leftKey, rightKey);

            if (comparison < 0) {
                leftMatches.Clear();
                rightMatches.RemoveAll(rm => {
                    var rmKey = rightKeySelector(rm);
                    return keyComparer.Compare(leftKey, rmKey) != 0;
                });
                if (rightMatches.Count > 0) {
                    leftMatches.Add(leftEnumerator.Current);
                    foreach (var leftItem in leftMatches)
                    foreach (var rightItem in rightMatches)
                        yield return (leftItem, rightItem);
                }
                else
                    yield return (leftEnumerator.Current, default);
                leftHasNext = leftEnumerator.MoveNext();
            }
            else if (comparison > 0) {
                rightMatches.Clear();
                leftMatches.RemoveAll(lm => {
                    var lmKey = leftKeySelector(lm);
                    return keyComparer.Compare(lmKey, rightKey) != 0;
                });
                if (leftMatches.Count > 0) {
                    rightMatches.Add(rightEnumerator.Current);
                    foreach (var leftItem in leftMatches)
                    foreach (var rightItem in rightMatches)
                        yield return (leftItem, rightItem);
                }
                else
                    yield return (default, rightEnumerator.Current);
                rightHasNext = rightEnumerator.MoveNext();
            }
            else {
                // Keys match, collect all matching elements
                leftMatches.RemoveAll(lm => {
                    var lmKey = leftKeySelector(lm);
                    return keyComparer.Compare(lmKey, rightKey) != 0;
                });
                rightMatches.RemoveAll(rm => {
                    var rmKey = rightKeySelector(rm);
                    return keyComparer.Compare(leftKey, rmKey) != 0;
                });

                // Store the matching key
                var matchingKey = leftKey;

                // Collect current and all subsequent left elements with the same key
                leftMatches.Add(leftEnumerator.Current);
                leftHasNext = leftEnumerator.MoveNext();

                // Add all subsequent left elements with the same key
                while (leftHasNext) {
                    var nextLeftKey = leftKeySelector(leftEnumerator.Current);
                    if (keyComparer.Compare(matchingKey, nextLeftKey) == 0)
                    {
                        leftMatches.Add(leftEnumerator.Current);
                        leftHasNext = leftEnumerator.MoveNext();
                    }
                    else
                        break;
                }

                // Collect current and all subsequent right elements with the same key
                rightMatches.Add(rightEnumerator.Current);
                rightHasNext = rightEnumerator.MoveNext();

                // Add all subsequent right elements with the same key
                while (rightHasNext)
                {
                    var nextRightKey = rightKeySelector(rightEnumerator.Current);
                    if (keyComparer.Compare(matchingKey, nextRightKey) == 0)
                    {
                        rightMatches.Add(rightEnumerator.Current);
                        rightHasNext = rightEnumerator.MoveNext();
                    }
                    else
                        break;
                }

                // Create all combinations of matches
                // This ensures that all duplicates are properly paired
                foreach (var leftItem in leftMatches)
                foreach (var rightItem in rightMatches)
                    yield return (leftItem, rightItem);
            }
        }

        // Process any remaining elements in the left sequence
        while (leftHasNext) {
            var lValue = leftEnumerator.Current;
            var leftKey = leftKeySelector(lValue);
            rightMatches.RemoveAll(rm => {
                var rmKey = rightKeySelector(rm);
                return keyComparer.Compare(leftKey, rmKey) != 0;
            });
            if (rightMatches.Count > 0)
                foreach (var rightItem in rightMatches)
                    yield return (lValue, rightItem);
            else
                yield return (leftEnumerator.Current, default);
            leftHasNext = leftEnumerator.MoveNext();
        }

        // Process any remaining elements in the right sequence
        while (rightHasNext) {
            var rValue = rightEnumerator.Current;
            var rightKey = rightKeySelector(rValue);
            leftMatches.RemoveAll(lm => {
                var lmKey = leftKeySelector(lm);
                return keyComparer.Compare(lmKey, rightKey) != 0;
            });
            if (leftMatches.Count > 0)
                foreach (var leftItem in leftMatches)
                    yield return (leftItem, rValue);
            else
                yield return (default, rightEnumerator.Current);
            rightHasNext = rightEnumerator.MoveNext();
        }
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

    public static async Task<List<T>> Flatten<T>(this Task<T[][]> task)
    {
        var arrays = await task.ConfigureAwait(false);
        return arrays.SelectMany(x => x).ToList();
    }

    public static async Task<List<T>> Flatten<T>(this Task<ApiArray<T[]>> task)
    {
        var arrays = await task.ConfigureAwait(false);
        return arrays.SelectMany(x => x).ToList();
    }

    public static async Task<List<T>> Flatten<T>(this Task<IReadOnlyList<T>[]> task)
    {
        var arrays = await task.ConfigureAwait(false);
        return arrays.SelectMany(x => x).ToList();
    }

    /// <summary>
    /// Ensures a sequence is strictly increasing by removing duplicates and any items less than or equal to previous ones
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence</typeparam>
    /// <param name="source">The source sequence</param>
    /// <param name="comparer">Custom comparer (optional). If null, defaults to <c>Comparer.Default</c> </param>
    /// <returns>A strictly increasing sequence</returns>
    public static IEnumerable<T> EnsureMonotonic<T>(this IEnumerable<T> source, IComparer<T>? comparer = null)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        comparer ??= Comparer<T>.Default;

        using var enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext())
            yield break;

        var last = enumerator.Current;
        yield return last;

        while (enumerator.MoveNext()) {
            var current = enumerator.Current;
            if (comparer.Compare(current, last) <= 0)
                continue;

            last = current;
            yield return current;
        }
    }

    public static IEnumerable<TResult> Scan<TSource, TResult>(
        this IEnumerable<TSource> source,
        Func<TResult, TSource, TResult> accumulator,
        TResult seed)
    {
        var current = seed;
        foreach (var item in source) {
            current = accumulator(current, item);
            yield return current;
        }
    }

    // Nested types

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
