namespace ActualChat.Collections;

public static class ListExt
{
    public static T GetOrDefault<T>(this IReadOnlyList<T> list, int index, T @default = default!)
        => index < 0 ? @default
            : index >= list.Count ? @default
            : list[index];

    public static T GetRandom<T>(this IReadOnlyList<T> list)
        => list[Random.Shared.Next(list.Count)];
    public static T GetRandom<T>(this IReadOnlyList<T> list, Random random)
        => list[random.Next(list.Count)];

    public static List<T> AddMany<T>(this List<T> list, T item, int count)
    {
        for (var i = 0; i < count; i++)
            list.Add(item);
        return list;
    }

    // Both lists have to be ordered!
    public static SetDiff<T> OrderedDiffFrom<T>(
        this IReadOnlyList<T> newItems,
        IReadOnlyList<T> oldItems,
        IComparer<T>? comparer = null)
        where T : IComparable<T>
    {
        comparer ??= Comparer<T>.Default;
        var added = ArrayBuffer<T>.Lease(false);
        var removed = ArrayBuffer<T>.Lease(false);
        try {
            int oldIndex = 0, newIndex = 0;
            while (oldIndex < oldItems.Count && newIndex < newItems.Count) {
                var oldItem = oldItems[oldIndex];
                var newItem = newItems[newIndex];

                var comparison = comparer.Compare(oldItem, newItem);
                switch (comparison) {
                case 0:
                    // Items are equal, move to the next item in both lists
                    oldIndex++;
                    newIndex++;
                    break;
                case < 0:
                    // Old item is smaller, it was removed
                    removed.Add(oldItem);
                    oldIndex++;
                    break;
                default:
                    // New item is smaller, it was added
                    added.Add(newItem);
                    newIndex++;
                    break;
                }
            }

            // Add remaining items
            for (; newIndex < newItems.Count; newIndex++)
                added.Add(newItems[newIndex]);
            for (; oldIndex < oldItems.Count; oldIndex++)
                removed.Add(oldItems[oldIndex]);

            return added.Count == 0 && removed.Count == 0
                ? SetDiff<T>.Unchanged
                : new SetDiff<T>(new ApiArray<T>(added.ToArray()), new ApiArray<T>(removed.ToArray()));
        }
        finally {
            added.Release();
            removed.Release();
        }
    }
}
