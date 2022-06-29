using System.Text;

namespace ActualChat.Chat;

public class ToStringVisitor : AsyncMarkupVisitor<Unit>
{
    public Func<string, Task<string>> AuthorNameResolver { get; init; }
    public Func<string, Task<string>> UserNameResolver { get; init; }
    public StringBuilder Builder { get; set; }
    public int MaxLength { get; init; }

    public ToStringVisitor(
        Func<string,Task<string>> authorNameResolver,
        Func<string,Task<string>> userNameResolver,
        int maxLength = int.MaxValue)
    {
        AuthorNameResolver = authorNameResolver;
        UserNameResolver = userNameResolver;
        MaxLength = maxLength;
        Builder = new StringBuilder();
    }

    public async Task<string> ToString(Markup markup, CancellationToken cancellationToken)
    {
        await Visit(markup, cancellationToken).ConfigureAwait(false);
        return Builder.ToString();
    }

    protected override async ValueTask<Unit> VisitSeq(MarkupSeq markup, CancellationToken cancellationToken)
    {
        foreach (var markupItem in markup.Items) {
            if (Builder.Length >= MaxLength)
                return Unit.Default;

            await Visit(markupItem, cancellationToken).ConfigureAwait(false);
        }
        return Unit.Default;
    }

    protected override ValueTask<Unit> VisitStylized(StylizedMarkup markup, CancellationToken cancellationToken)
        => Visit(markup.Content, cancellationToken);

    protected override ValueTask<Unit> VisitUrl(UrlMarkup markup, CancellationToken cancellationToken)
    {
        if (Builder.Length >= MaxLength)
            return ValueTask.FromResult(Unit.Default);

        Builder.Append(markup.Url);
        return ValueTask.FromResult(Unit.Default);
    }

    protected override async ValueTask<Unit> VisitMention(Mention markup, CancellationToken cancellationToken)
    {
        if (Builder.Length >= MaxLength)
            return Unit.Default;

        Builder.Append("@");
        Builder.Append(await (markup.Kind switch {
                MentionKind.AuthorId => AuthorNameResolver(markup.Target),
                MentionKind.UserId => UserNameResolver(markup.Target),
                _ => Task.FromResult(markup.Target),
            }));
        return Unit.Default;
    }

    protected override ValueTask<Unit> VisitCodeBlock(CodeBlockMarkup markup, CancellationToken cancellationToken)
    {
        if (Builder.Length >= MaxLength)
            return ValueTask.FromResult(Unit.Default);

        Builder.Append(markup.Code);
        return ValueTask.FromResult(Unit.Default);
    }

    protected override ValueTask<Unit> VisitPlainText(PlainTextMarkup markup, CancellationToken cancellationToken)
    {
        if (Builder.Length >= MaxLength)
            return ValueTask.FromResult(Unit.Default);

        Builder.Append(markup.Text);
        return ValueTask.FromResult(Unit.Default);
    }

    protected override ValueTask<Unit> VisitPlayableText(PlayableTextMarkup markup, CancellationToken cancellationToken)
    {
        if (Builder.Length >= MaxLength)
            return ValueTask.FromResult(Unit.Default);

        Builder.Append(markup.Text);
        return ValueTask.FromResult(Unit.Default);
    }

    protected override ValueTask<Unit> VisitPreformattedText(PreformattedTextMarkup markup, CancellationToken cancellationToken)
    {
        if (Builder.Length >= MaxLength)
            return ValueTask.FromResult(Unit.Default);

        Builder.Append(markup.Text);
        return ValueTask.FromResult(Unit.Default);
    }

    protected override ValueTask<Unit> VisitUnparsed(UnparsedTextMarkup markup, CancellationToken cancellationToken)
    {
        if (Builder.Length >= MaxLength)
            return ValueTask.FromResult(Unit.Default);

        Builder.Append(markup.Text);
        return ValueTask.FromResult(Unit.Default);
    }
}
