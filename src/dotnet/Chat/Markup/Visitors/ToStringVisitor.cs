namespace ActualChat.Chat;

public class ToStringVisitor : AsyncMarkupVisitor<string>
{
    public Func<string, Task<string>> AuthorNameResolver { get; init; }
    public Func<string, Task<string>> UserNameResolver { get; init; }

    public ToStringVisitor(
        Func<string,Task<string>> authorNameResolver,
        Func<string,Task<string>> userNameResolver)
    {
        AuthorNameResolver = authorNameResolver;
        UserNameResolver = userNameResolver;
    }

    protected override async ValueTask<string> VisitSeq(MarkupSeq markup, CancellationToken cancellationToken)
    {
        var items = await Task.WhenAll(markup.Items.Select(m => Visit(m, cancellationToken).AsTask()));
        return string.Join(", ", items);
    }

    protected override ValueTask<string> VisitStylized(StylizedMarkup markup, CancellationToken cancellationToken)
        => Visit(markup.Content, cancellationToken);

    protected override ValueTask<string> VisitUrl(UrlMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult(markup.Url);

    protected override async ValueTask<string> VisitMention(Mention markup, CancellationToken cancellationToken)
        => await (markup.Kind switch {
            MentionKind.AuthorId => AuthorNameResolver(markup.Target),
            MentionKind.UserId => UserNameResolver(markup.Target),
            _ => Task.FromResult(markup.Target),
        });

    protected override ValueTask<string> VisitCodeBlock(CodeBlockMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult(markup.Code);

    protected override ValueTask<string> VisitPlainText(PlainTextMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult(markup.Text);

    protected override ValueTask<string> VisitPlayableText(PlayableTextMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult(markup.Text);

    protected override ValueTask<string> VisitPreformattedText(PreformattedTextMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult(markup.Text);

    protected override ValueTask<string> VisitUnparsed(UnparsedTextMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult(markup.Text);
}
