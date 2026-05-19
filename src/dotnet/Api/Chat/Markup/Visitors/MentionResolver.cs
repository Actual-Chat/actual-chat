namespace ActualChat.Chat;

/// <summary>
/// Markup-tree rewriter that replaces each <see cref="MentionMarkup"/> with its enriched
/// typed subclass (e.g. <see cref="AuthorMention"/> with a populated <c>Author</c>) so the
/// renderer can read pre-resolved data synchronously. Driven by <see cref="IChatMentionResolver.Enrich"/>.
/// </summary>
public interface IMentionResolver
{
    ValueTask<Markup> Apply(Markup markup, CancellationToken cancellationToken);
}

public record MentionResolver(IChatMentionResolver ChatMentionResolver) : AsyncMarkupRewriter, IMentionResolver
{
    public ValueTask<Markup> Apply(Markup markup, CancellationToken cancellationToken)
        => Visit(markup, cancellationToken);

    protected override async ValueTask<Markup> VisitMention(MentionMarkup markup, CancellationToken cancellationToken)
    {
        var enriched = await ChatMentionResolver.Enrich(markup, cancellationToken).ConfigureAwait(false);
        return ReferenceEquals(enriched, markup) ? markup : enriched;
    }
}
