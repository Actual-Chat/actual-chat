using System.Text;

namespace ActualChat.Chat;

public class MarkupToTextConverter : AsyncMarkupVisitor<Unit>
{
    public Func<string, CancellationToken, Task<string>> GetAuthorName { get; init; }
    public Func<string, CancellationToken, Task<string>> GetUserName { get; init; }
    public StringBuilder Builder { get; set; }
    public int MaxLength { get; init; }

    public MarkupToTextConverter(
        Func<string, CancellationToken, Task<string>> getAuthorName,
        Func<string, CancellationToken,Task<string>> getUserName,
        int maxLength = int.MaxValue)
    {
        GetAuthorName = getAuthorName;
        GetUserName = getUserName;
        MaxLength = maxLength;
        Builder = new StringBuilder();
    }

    public async Task<string> Apply(Markup markup, CancellationToken cancellationToken)
    {
        await Visit(markup, cancellationToken).ConfigureAwait(false);
        return Builder.ToString();
    }

    protected override async ValueTask<Unit> VisitSeq(MarkupSeq markup, CancellationToken cancellationToken)
    {
        foreach (var markupItem in markup.Items) {
            if (Builder.Length >= MaxLength)
                return default;

            await Visit(markupItem, cancellationToken).ConfigureAwait(false);
        }
        return default;
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
            return default;

        Builder.Append("@");
        Builder.Append(
            await (markup.Kind switch {
                MentionKind.AuthorId => GetAuthorName(markup.Target, cancellationToken),
                MentionKind.UserId => GetUserName(markup.Target, cancellationToken),
                _ => Task.FromResult(markup.Target),
            }).ConfigureAwait(false));
        return default;
    }

    protected override ValueTask<Unit> VisitCodeBlock(CodeBlockMarkup markup, CancellationToken cancellationToken)
    {
        if (Builder.Length >= MaxLength)
            return ValueTask.FromResult(Unit.Default);

        Builder.Append(markup.Code);
        return ValueTask.FromResult(Unit.Default);
    }

    protected override ValueTask<Unit> VisitPlainText(PlainTextMarkup markup, CancellationToken cancellationToken)
        => VisitText(markup, cancellationToken);

    protected override ValueTask<Unit> VisitNewLine(NewLineMarkup newLineMarkup, CancellationToken cancellationToken)
        => VisitText(newLineMarkup, cancellationToken);

    protected override ValueTask<Unit> VisitPlayableText(PlayableTextMarkup markup, CancellationToken cancellationToken)
        => VisitText(markup, cancellationToken);

    protected override ValueTask<Unit> VisitPreformattedText(PreformattedTextMarkup markup, CancellationToken cancellationToken)
        => VisitText(markup, cancellationToken);

    protected override ValueTask<Unit> VisitUnparsed(UnparsedTextMarkup markup, CancellationToken cancellationToken)
        => VisitText(markup, cancellationToken);

    protected override ValueTask<Unit> VisitText(TextMarkup markup, CancellationToken cancellationToken)
    {
        if (Builder.Length >= MaxLength)
            return ValueTask.FromResult(Unit.Default);

        Builder.Append(markup.Text);
        return ValueTask.FromResult(Unit.Default);
    }
}
