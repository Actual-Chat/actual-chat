using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public class MarkupToEditorHtmlConverter
{
    private static readonly Regex NewLineRegex = new(@"\r?\n", RegexOptions.Compiled);

    public MarkupHub MarkupHub { get; }

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
            AddHtml("<span class=\"editor-mention\" contenteditable=\"false\" data-id=\"");
            AddPlainText(id);
            AddHtml("\">");
            AddHiddenHtml("@`");
            AddPlainText(markup.Name);
            AddHiddenHtml("`" + id.HtmlEncode());
            AddHtml("</span>");
            return default;
        }

        protected override Unit VisitCodeBlock(CodeBlockMarkup markup)
            => AddPreformattedMarkupText(markup);

        protected override Unit VisitPlainText(PlainTextMarkup markup)
            => AddMarkupText(markup);

        protected override Unit VisitNewLine(NewLineMarkup markup)
            => AddHtml("\n");

        protected override Unit VisitPlayableText(PlayableTextMarkup markup)
            => AddMarkupText(markup);

        protected override Unit VisitPreformattedText(PreformattedTextMarkup markup)
            => AddPreformattedMarkupText(markup);

        protected override Unit VisitUnparsed(UnparsedTextMarkup markup)
            => AddMarkupText(markup);

        protected override Unit VisitUnknown(Markup markup)
            => AddMarkupText(markup);

        // Private methods

        private Unit AddMarkupText(Markup markup)
            => AddPlainText(markup.Format());

        private Unit AddPreformattedMarkupText(Markup markup)
        {
            AddHtml("<span class=\"editor-preformatted\">");
            AddMarkupText(markup);
            AddHtml("</span>");
            return default;
        }

        private Unit AddPlainText(string text)
        {
            var html = HtmlEncoder.Default.Encode(text);
            // html = html.Replace('\n', '\u2028');
            // html = NewLineRegex.Replace(html, "<br/>");
            AddHtml(html);
            return default;
        }

        private Unit AddHiddenHtml(string html)
        {
            _result.Append("<span class=\"editor-hidden\">");
            _result.Append(html);
            _result.Append("</span>");
            return default;
        }

        private Unit AddHtml(string html)
        {
            _result.Append(html);
            return default;
        }
    }
}
