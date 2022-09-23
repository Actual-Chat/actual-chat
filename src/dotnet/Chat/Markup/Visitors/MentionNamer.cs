namespace ActualChat.Chat;

public class MentionNamer : AsyncMarkupRewriter
{
    public IMentionResolver<string> MentionResolver { get; }
    public Func<MentionMarkup, MentionMarkup> UnresolvedMentionRewriter { get; init; } =
        m => m with { Name = m.NameOrNotAvailable };

    public MentionNamer(IMentionResolver<string> mentionResolver)
        => MentionResolver = mentionResolver;

    protected override async ValueTask<Markup> VisitMention(MentionMarkup markup, CancellationToken cancellationToken)
    {
        var targetName = await MentionResolver.Resolve(markup, cancellationToken).ConfigureAwait(false);
        if (targetName == null)
            return UnresolvedMentionRewriter.Invoke(markup);

        return OrdinalEquals(markup.Name, targetName)
            ? markup
            : markup with { Name = targetName };
    }
}
