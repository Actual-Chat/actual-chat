using System.Text;
using System.Text.Encodings.Web;
using System.Web;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;
using NetBox.Extensions;

namespace ActualChat.Chat.UI.Blazor.Components;

public class MarkupToEditorHtmlConverter
{
    private static ValueTask<Unit> UnitValueTask { get; } = Stl.Async.TaskExt.UnitTask.ToValueTask();

    private MarkupHub MarkupHub { get; }

    public MarkupToEditorHtmlConverter(MarkupHub markupHub)
        => MarkupHub = markupHub;

    public async Task<string> Convert(string markupText, CancellationToken cancellationToken)
    {
        var visitor = new Visitor();
        var markup = MarkupHub.MarkupParser.Parse(markupText);
        markup = await MarkupHub.MentionNamer.Rewrite(markup, cancellationToken).ConfigureAwait(false);
        var result = visitor.Apply(markup);
        return result;
    }

    private class Visitor : MarkupVisitor<Unit>
    {
        private readonly StringBuilder _result = new();

        public string Apply(Markup markup)
        {
            if (_result.Length > 0)
                throw StandardError.StateTransition("This method can be called just once per visitor instance.");
            Visit(markup);
            return _result.ToString();
        }

        protected override Unit VisitSeq(MarkupSeq markup)
        {
            foreach (var markupItem in markup.Items)
                Visit(markupItem);
            return default;
        }

        protected override Unit VisitStylized(StylizedMarkup markup)
        {
            var startTag = markup.Style switch {
                TextStyle.Italic => "<em>",
                TextStyle.Bold => "<strong>",
                _ => "",
            };
            var endTag = markup.Style switch {
                TextStyle.Italic => "</em>",
                TextStyle.Bold => "</strong>",
                _ => "",
            };

            AddHtml(startTag);
            AddPlainText(markup.StyleToken);
            Visit(markup.Content);
            AddPlainText(markup.StyleToken);
            AddHtml(endTag);
            return default;
        }

        protected override Unit VisitUrl(UrlMarkup markup)
        {
            AddHtml("<a target=\"_blank\" href=\"");
            AddPlainText(markup.Url);
            AddHtml("\">");
            AddMarkupText(markup);
            AddHtml("</a>");
            return default;
        }

        protected override Unit VisitMention(Mention markup)
        {
            var id = markup.Id;
            AddHtml("<span class=\"mention-markup\" contenteditable=\"false\" data-id=\"");
            AddPlainText(id);
            AddHtml("\">");
            AddPlainText(markup.Name);
            AddHtml("</span>");
            return default;
        }

        protected override Unit VisitCodeBlock(CodeBlockMarkup markup)
        {
            AddHtml("<pre class=\"code-block-markup\"><code>");
            AddMarkupText(markup);
            AddHtml("</code></pre>");
            return default;
        }

        protected override Unit VisitPlainText(PlainTextMarkup markup)
            => AddMarkupText(markup);

        protected override Unit VisitNewLine(NewLineMarkup markup)
            => AddHtml("</br>");

        protected override Unit VisitPlayableText(PlayableTextMarkup markup)
            => AddMarkupText(markup);

        protected override Unit VisitPreformattedText(PreformattedTextMarkup markup)
        {
            AddHtml("<code class=\"preformatted-text-markup\">");
            AddMarkupText(markup);
            AddHtml("</code>");
            return default;
        }

        protected override Unit VisitUnparsed(UnparsedTextMarkup markup)
            => AddMarkupText(markup);

        protected override Unit VisitText(TextMarkup markup)
            => AddMarkupText(markup);

        // Private methods

        private Unit AddMarkupText(Markup markup)
            => AddPlainText(markup.Format());

        private Unit AddPlainText(string text)
        {
            var html = HtmlEncoder.Default.Encode(text);
            AddHtml(html);
            return default;
        }

        private Unit AddHtml(string html)
        {
            _result.Append(html);
            return default;
        }
    }
}
