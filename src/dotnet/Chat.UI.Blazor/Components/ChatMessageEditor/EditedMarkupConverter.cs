using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public class EditedMarkupConverter
{
    private readonly IMarkupParser _markupParser;
    private readonly MentionNameResolver _mentionNameResolver;

    public EditedMarkupConverter(
        IMarkupParser markupParser,
        MentionNameResolver mentionNameResolver)
    {
        _markupParser = markupParser;
        _mentionNameResolver = mentionNameResolver;
    }

    public async Task<ImmutableArray<EditorNode>> Convert(
        string markdown,
        CancellationToken cancellationToken)
    {
        var visitor = new Visitor(_mentionNameResolver.GetName);
        var markup = _markupParser.Parse(markdown);
        return await visitor.Apply(markup, cancellationToken).ConfigureAwait(false);
    }

    private class Visitor : AsyncMarkupVisitor<Unit>
    {
        private readonly List<EditorNode> _nodes = new ();
        private Func<MentionKind, string, CancellationToken, Task<string>> GetName { get; }

        public Visitor(Func<MentionKind, string, CancellationToken, Task<string>> getName)
        {
            GetName = getName;
        }

        public async Task<ImmutableArray<EditorNode>> Apply(Markup markup, CancellationToken cancellationToken)
        {
            await Visit(markup, cancellationToken).ConfigureAwait(false);
            return _nodes.ToImmutableArray();
        }

        protected override async ValueTask<Unit> VisitSeq(MarkupSeq markup, CancellationToken cancellationToken)
        {
            foreach (var markupItem in markup.Items)
                await Visit(markupItem, cancellationToken).ConfigureAwait(false);

            return Unit.Default;
        }

        protected override async ValueTask<Unit> VisitStylized(StylizedMarkup markup, CancellationToken cancellationToken)
        {
            var token = markup.GetWrapToken();
            AddParagraph(token);
            await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
            AddParagraph(token);

            return Unit.Default;
        }

        protected override ValueTask<Unit> VisitUrl(UrlMarkup markup, CancellationToken cancellationToken)
            => AddParagraph(markup);

        protected override async ValueTask<Unit> VisitMention(Mention markup, CancellationToken cancellationToken)
        {
            var id = markup.Target;
            var name = await GetName(markup.Kind, id, cancellationToken).ConfigureAwait(false);
            _nodes.Add(new ("mention", markup.ToMarkupText().TrimStart('@'), name));

            return Unit.Default;
        }

        protected override ValueTask<Unit> VisitCodeBlock(CodeBlockMarkup markup, CancellationToken cancellationToken)
            => AddParagraph(markup);

        protected override ValueTask<Unit> VisitPlainText(PlainTextMarkup markup, CancellationToken cancellationToken)
            => AddParagraph(markup);

        protected override ValueTask<Unit> VisitNewLine(NewLineMarkup markup, CancellationToken cancellationToken)
            => AddParagraph(markup);

        protected override ValueTask<Unit> VisitPlayableText(
            PlayableTextMarkup markup,
            CancellationToken cancellationToken)
            => AddParagraph(markup);

        protected override ValueTask<Unit> VisitPreformattedText(
            PreformattedTextMarkup markup,
            CancellationToken cancellationToken)
            => AddParagraph(markup);

        protected override ValueTask<Unit> VisitUnparsed(UnparsedTextMarkup markup, CancellationToken cancellationToken)
            => AddParagraph(markup);

        protected override ValueTask<Unit> VisitText(TextMarkup markup, CancellationToken cancellationToken)
            => AddParagraph(markup);

        private ValueTask<Unit> AddParagraph(Markup markup)
        {
            AddParagraph(markup.ToMarkupText());
            return ValueTask.FromResult(Unit.Default);
        }

        private void AddParagraph(string text)
            => _nodes.Add(new("paragraph", text));
    }

    /// <summary>
    /// Json container for JsInterop
    /// </summary>
    public record EditorNode(string Type, string Content, string? DisplayContent = null);
}
