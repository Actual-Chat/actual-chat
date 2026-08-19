using ActualChat.UI.Blazor.Resources;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Words system entries in the UI language. The author name renders as its own markup node,
/// so every catalog value here is what follows it.
/// </summary>
public sealed class LocalizedSystemEntryMarkupBuilder(IServiceProvider services) : SystemEntryMarkupBuilder
{
    private IStringLocalizer L => field ??= services.GetRequiredService<IStringLocalizer>();
    protected override string SomeoneName => L.SystemEntry_Someone;

    // Protected methods

    protected override Markup BuildMembersChanged(MembersChangedEntry entry)
    {
        var authorName = entry.TargetAuthorName.NullIfEmpty() ?? SomeoneName;
        var text = entry.HasLeft ? L.SystemEntry_MemberLeft : L.SystemEntry_MemberJoined;
        return entry.TargetAuthorId is null
            ? new PlainTextMarkup(authorName + text)
            : new MarkupSeq(
                new AuthorMention(MentionRef.NewAuthor(entry.TargetAuthorId), authorName),
                new PlainTextMarkup(text));
    }

    protected override Markup BuildNotifyMembers(NotifyMembersEntry entry)
    {
        var authorName = entry.TargetAuthorName.NullIfEmpty() ?? SomeoneName;
        var text = L.SystemEntry_AttentionRequested;
        return entry.TargetAuthorId is null
            ? new PlainTextMarkup(authorName + text)
            : new MarkupSeq(
                new AuthorMention(MentionRef.NewAuthor(entry.TargetAuthorId), authorName),
                new PlainTextMarkup(text));
    }
}
