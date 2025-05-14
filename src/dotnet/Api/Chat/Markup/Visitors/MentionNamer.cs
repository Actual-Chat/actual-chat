namespace ActualChat.Chat;

public interface IMentionNamer
{
    ValueTask<Markup> Apply(Markup markup, CancellationToken cancellationToken);
}

public record MentionNamer(IMentionResolver<string> MentionResolver) : AsyncMarkupRewriter, IMentionNamer
{
    public Func<MentionMarkup, MentionMarkup> UnresolvedMentionRewriter { get; init; } =
        m => new MentionMarkup(m.Id, m.NameOrNotAvailable);

    public ValueTask<Markup> Apply(Markup markup, CancellationToken cancellationToken)
        => Visit(markup, cancellationToken);

    protected override async ValueTask<Markup> VisitMention(MentionMarkup markup, CancellationToken cancellationToken)
    {
        var targetName = await MentionResolver.Resolve(markup, cancellationToken).ConfigureAwait(false);
        if (targetName is null)
            return UnresolvedMentionRewriter.Invoke(markup);

        return OrdinalEquals(markup.Name, targetName)
            ? markup
            : new MentionMarkup(markup.Id, targetName);
    }
}
