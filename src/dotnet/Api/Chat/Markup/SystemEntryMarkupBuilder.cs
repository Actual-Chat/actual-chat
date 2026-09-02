namespace ActualChat.Chat;

/// <summary>
/// Builds the markup shown for a <see cref="SystemEntry"/>. The author name renders as its own
/// markup node, so every word a subclass supplies is what follows it.
/// </summary>
public abstract class SystemEntryMarkupBuilder
{
    protected abstract string SomeoneName { get; }
    protected abstract string MemberJoined { get; }
    protected abstract string MemberLeft { get; }
    protected abstract string AttentionRequested { get; }

    public Markup Build(SystemEntry entry)
        => entry switch {
            MembersChangedEntry e => BuildMembersChanged(e),
            NotifyMembersEntry e => BuildNotifyMembers(e),
            // SystemEntryLocalizationTest fails on any [Union] kind that lands here
            _ => Markup.EmptyText,
        };

    // Private methods

    private Markup BuildMembersChanged(MembersChangedEntry entry)
    {
        var authorName = entry.TargetAuthorName.NullIfEmpty() ?? SomeoneName;
        var text = entry.HasLeft ? MemberLeft : MemberJoined;
        return entry.TargetAuthorId is null
            ? new PlainTextMarkup(authorName + text)
            : new MarkupSeq(
                new AuthorMention(MentionRef.NewAuthor(entry.TargetAuthorId), authorName),
                new PlainTextMarkup(text));
    }

    private Markup BuildNotifyMembers(NotifyMembersEntry entry)
    {
        var authorName = entry.TargetAuthorName.NullIfEmpty() ?? SomeoneName;
        var text = AttentionRequested;
        return entry.TargetAuthorId is null
            ? new PlainTextMarkup(authorName + text)
            : new MarkupSeq(
                new AuthorMention(MentionRef.NewAuthor(entry.TargetAuthorId), authorName),
                new PlainTextMarkup(text));
    }
}
