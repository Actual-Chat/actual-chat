namespace ActualChat.Chat;

// The English here is a deliberate second copy of the SystemEntry_* keys in Strings.en.json:
// ActualChat.Api ships as a NuGet package and must word an entry without the catalog, which it
// can't reference. SystemEntryLocalizationTest keeps the two copies equal.

/// <summary>
/// Builds the markup shown for a <see cref="SystemEntry"/>. The wording here is English;
/// hosts that know the UI language override the per-kind methods with catalog values.
/// </summary>
public class SystemEntryMarkupBuilder
{
    public static readonly SystemEntryMarkupBuilder Default = new();

    protected virtual string SomeoneName => "Someone";

    public Markup Build(SystemEntry entry)
        => entry switch {
            MembersChangedEntry e => BuildMembersChanged(e),
            NotifyMembersEntry e => BuildNotifyMembers(e),
            CallEntry e => BuildCall(e),
            // A system event of a kind this build has no member for renders as nothing: a system
            // event is low-value by construction, so "update your app" in its place is just a nag.
            UnsupportedSystemEntry => Markup.EmptyText,
            // SystemEntryLocalizationTest fails on any [Union] kind that lands here
            _ => Markup.EmptyText,
        };

    // Protected methods

    protected virtual Markup BuildMembersChanged(MembersChangedEntry entry)
    {
        var authorName = entry.TargetAuthorName.NullIfEmpty() ?? SomeoneName;
        var verb = entry.HasLeft ? "left" : "joined";
        return entry.TargetAuthorId is null
            ? new PlainTextMarkup($"{authorName} has {verb} the chat.")
            : new MarkupSeq(
                new AuthorMention(MentionRef.NewAuthor(entry.TargetAuthorId), authorName),
                new PlainTextMarkup($" has {verb} the chat."));
    }

    protected virtual Markup BuildNotifyMembers(NotifyMembersEntry entry)
    {
        var authorName = entry.TargetAuthorName.NullIfEmpty() ?? SomeoneName;
        return entry.TargetAuthorId is null
            ? new PlainTextMarkup($"{authorName} asked for attention.")
            : new MarkupSeq(
                new AuthorMention(MentionRef.NewAuthor(entry.TargetAuthorId), authorName),
                new PlainTextMarkup(" asked for attention."));
    }

    protected virtual Markup BuildCall(CallEntry entry)
    {
        var callerName = entry.CallerName.NullIfEmpty() ?? SomeoneName;
        return new MarkupSeq(
            new AuthorMention(MentionRef.NewAuthor(entry.CallerId), callerName),
            new PlainTextMarkup(entry.Outcome switch {
                CallOutcome.NoAnswer => " called. No answer.",
                CallOutcome.Declined => " called. Declined.",
                CallOutcome.Canceled => " called. Canceled.",
                _ => " called.",
            }));
    }
}
