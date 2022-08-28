using System.Text.Encodings.Web;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public class MarkupToEditorHtmlConverter
{
    private static ValueTask<Unit> UnitValueTask { get; } = Stl.Async.TaskExt.UnitTask.ToValueTask();

    private MarkupHub MarkupHub { get; }

    public MarkupToEditorHtmlConverter(MarkupHub markupHub)
        => MarkupHub = markupHub;

    public async Task<string> Convert(
        Symbol chatId,
        string markupText,
        CancellationToken cancellationToken)
    {
        MarkupHub.ChatId = chatId;
        var visitor = new Visitor(MarkupHub);
        var markup = MarkupHub.MarkupParser.Parse(markupText);
        return await visitor.Apply(markup, cancellationToken).ConfigureAwait(false);
    }

    private class Visitor : AsyncMarkupVisitor<Unit>
    {
        private readonly List<string> _result = new();

        private MarkupHub MarkupHub { get; }

        public Visitor(MarkupHub markupHub)
            => MarkupHub = markupHub;

        public async ValueTask<string> Apply(Markup markup, CancellationToken cancellationToken)
        {
            if (_result.Count != 0)
                throw StandardError.StateTransition("This method can be called just once per visitor instance.");
            await Visit(markup, cancellationToken).ConfigureAwait(false);
            return _result.ToDelimitedString("");
        }

        protected override async ValueTask<Unit> VisitSeq(MarkupSeq markup, CancellationToken cancellationToken)
        {
            foreach (var markupItem in markup.Items)
                await Visit(markupItem, cancellationToken).ConfigureAwait(false);
            return default;
        }

        protected override async ValueTask<Unit> VisitStylized(StylizedMarkup markup, CancellationToken cancellationToken)
        {
            AddPlainText(markup.StyleToken);
            await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
            AddPlainText(markup.StyleToken);
            return default;
        }

        protected override ValueTask<Unit> VisitUrl(UrlMarkup markup, CancellationToken cancellationToken)
            => AddMarkupText(markup, cancellationToken);

        protected override async ValueTask<Unit> VisitMention(Mention markup, CancellationToken cancellationToken)
        {
            var id = markup.Id;
            var name = await MarkupHub.ChatMentionResolver.ResolveName(markup, cancellationToken).ConfigureAwait(false);
            name ??= Author.Removed.Name;
            AddHtml("<span class=\"mention\" contenteditable=\"false\" data-id=\"");
            AddPlainText(id);
            AddHtml("\">");
            AddPlainText(name);
            AddHtml("</span>");
            return default;
        }

        protected override ValueTask<Unit> VisitCodeBlock(CodeBlockMarkup markup, CancellationToken cancellationToken)
            => AddMarkupText(markup, cancellationToken);

        protected override ValueTask<Unit> VisitPlainText(PlainTextMarkup markup, CancellationToken cancellationToken)
            => AddMarkupText(markup, cancellationToken);

        protected override ValueTask<Unit> VisitNewLine(NewLineMarkup markup, CancellationToken cancellationToken)
            => AddHtml("</br>", cancellationToken);

        protected override ValueTask<Unit> VisitPlayableText(PlayableTextMarkup markup, CancellationToken cancellationToken)
            => AddMarkupText(markup, cancellationToken);

        protected override ValueTask<Unit> VisitPreformattedText(PreformattedTextMarkup markup, CancellationToken cancellationToken)
            => AddMarkupText(markup, cancellationToken);

        protected override ValueTask<Unit> VisitUnparsed(UnparsedTextMarkup markup, CancellationToken cancellationToken)
            => AddMarkupText(markup, cancellationToken);

        protected override ValueTask<Unit> VisitText(TextMarkup markup, CancellationToken cancellationToken)
            => AddMarkupText(markup, cancellationToken);

        // Private methods

        private ValueTask<Unit> AddMarkupText(Markup markup, CancellationToken cancellationToken)
        {
            AddMarkupText(markup);
            return UnitValueTask;
        }

        private ValueTask<Unit> AddPlainText(string text, CancellationToken cancellationToken)
        {
            AddPlainText(text);
            return UnitValueTask;
        }

        private ValueTask<Unit> AddHtml(string html, CancellationToken cancellationToken)
        {
            AddHtml(html);
            return UnitValueTask;
        }

        private void AddMarkupText(Markup markup)
            => AddPlainText(markup.Format());

        private void AddPlainText(string text)
        {
            var html = HtmlEncoder.Default.Encode(text);
            AddHtml(html);
        }

        private void AddHtml(string html)
            => _result.Add(html);
    }
}
