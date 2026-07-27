namespace ActualChat.Chat;

/// <summary>
/// Compares <see cref="Conversation"/>s by content, for compute methods that rebuild one on every
/// recompute - <see cref="Conversation"/> itself compares by reference.
/// </summary>
public sealed class ConversationContentComparer : IEqualityComparer<Conversation>
{
    public bool Equals(Conversation? x, Conversation? y)
    {
        // Version is excluded on purpose: writes that don't touch the card still bump it.
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null)
            return false;

        return x.Id == y.Id
            && x.EndEntryLid == y.EndEntryLid
            && x.StartsAt == y.StartsAt
            && x.EndsAt == y.EndsAt
            && x.MessageCount == y.MessageCount
            && x.IsExpandedByDefault == y.IsExpandedByDefault
            && x.Title == y.Title
            && x.Description == y.Description
            && x.Summary == y.Summary
            && x.AuthorIds.SequenceEqual(y.AuthorIds);
    }

    public int GetHashCode(Conversation obj)
        => HashCode.Combine(obj.Id,
            obj.EndEntryLid,
            obj.EndsAt,
            obj.MessageCount,
            obj.AuthorIds.Count);
}
