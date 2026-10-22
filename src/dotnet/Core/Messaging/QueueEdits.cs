namespace ActualChat.Messaging;

/// <summary>
/// A batch of edits applied to a <see cref="PartitionedCommandQueue{TItem}"/> partition:
/// items are matched by reference, replacements keep their position, adds go to the tail.
/// </summary>
public sealed class QueueEdits<TItem>
    where TItem : class
{
    private readonly List<(TItem Original, TItem? Replacement)> _replacements = new();
    private readonly List<TItem> _adds = new();

    public QueueEdits<TItem> Replace(TItem original, TItem replacement)
    {
        _replacements.Add((original, replacement));
        return this;
    }

    public QueueEdits<TItem> Remove(TItem item)
    {
        _replacements.Add((item, null));
        return this;
    }

    public QueueEdits<TItem> Add(TItem item)
    {
        _adds.Add(item);
        return this;
    }

    internal void ApplyTo(List<TItem> waiting)
    {
        foreach (var (original, replacement) in _replacements) {
            var index = waiting.FindIndex(x => ReferenceEquals(x, original));
            if (index < 0)
                continue;

            if (replacement is null)
                waiting.RemoveAt(index);
            else
                waiting[index] = replacement;
        }
        waiting.AddRange(_adds);
    }
}
