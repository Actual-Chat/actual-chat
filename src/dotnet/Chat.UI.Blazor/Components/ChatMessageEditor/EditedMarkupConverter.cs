using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public class EditedMarkupConverter
{
    public MarkupHub MarkupHub { get; }

    public EditedMarkupConverter(MarkupHub markupHub)
        => MarkupHub = markupHub;

    public async Task<ImmutableArray<EditorNode>> Convert(string markupText, CancellationToken cancellationToken)
    {
        var visitor = new Visitor();
        var markup = MarkupHub.MarkupParser.Parse(markupText);
        markup = await MarkupHub.MentionNamer.Rewrite(markup, cancellationToken).ConfigureAwait(false);
        var result = visitor.Apply(markup);
        return result;
    }

    // Nested types

    private class Visitor : MarkupVisitor<Unit>
    {
        private readonly List<EditorNode> _nodes = new ();

        public ImmutableArray<EditorNode> Apply(Markup markup)
        {
            _nodes.Clear();
            Visit(markup);
            return _nodes.ToImmutableArray();
        }

        protected override Unit VisitSeq(MarkupSeq markup)
        {
            foreach (var markupItem in markup.Items)
                Visit(markupItem);

            return Unit.Default;
        }

        protected override Unit VisitStylized(StylizedMarkup markup)
        {
            var token = markup.StyleToken;
            AddParagraph(token);
            Visit(markup.Content);
            AddParagraph(token);
            return default;
        }

        protected override Unit VisitUrl(UrlMarkup markup)
            => AddParagraph(markup);

        protected override Unit VisitMention(Mention markup)
        {
            _nodes.Add(new ("mention", markup.Id, markup.Name));
            return default;
        }

        protected override Unit VisitCodeBlock(CodeBlockMarkup markup)
            => AddParagraph(markup);

        protected override Unit VisitPlainText(PlainTextMarkup markup)
            => AddParagraph(markup);

        protected override Unit VisitNewLine(NewLineMarkup markup)
            => AddParagraph(markup);

        protected override Unit VisitPlayableText(PlayableTextMarkup markup)
            => AddParagraph(markup);

        protected override Unit VisitPreformattedText(PreformattedTextMarkup markup)
            => AddParagraph(markup);

        protected override Unit VisitUnparsed(UnparsedTextMarkup markup)
            => AddParagraph(markup);

        protected override Unit VisitText(TextMarkup markup)
            => AddParagraph(markup);

        private Unit AddParagraph(Markup markup)
        {
            AddParagraph(markup.Format());
            return default;
        }

        private void AddParagraph(string text)
            => _nodes.Add(new("paragraph", text));
    }

    /// <summary>
    /// Json container for JsInterop
    /// </summary>
    public record EditorNode(string Type, string Content, string? DisplayContent = null);
}
