using System.Text;
using System.Text.RegularExpressions;

namespace ActualChat.Chat;

public abstract partial record MarkupHtmlFormatterBase : MarkupFormatterBase
{
    [GeneratedRegex(@"\r?\n")]
    private static partial Regex NewLineRegexFactory();

    private static readonly Regex NewLineRegex = NewLineRegexFactory();

    public string UrlClass { get; init; } = "markup-url";
    public string MentionClass { get; init; } = "markup-mention";
    public string CodeBlockClass { get; init; } = "markup-code";
    public string ParagraphClass { get; init; } = "markup-paragraph";
    public string TableClass { get; init; } = "markup-table";
    public string HeaderClass { get; init; } = "markup-header";
    public string PreformattedTextClass { get; init; } = "markup-preformatted-text";
    public string NewLineHtml { get; init; } = "<br/>";
    public string? NewLineReplacement { get; init; } = null;

    protected override void VisitStylized(StylizedMarkup markup, ref StringBuilder state)
    {
        if (markup.Style == TextStyle.Spoiler) {
            // Server-rendered HTML has no reveal affordance, so the content is masked.
            AddText(StylizedMarkup.Mask(markup.Content.ToReadableText()), ref state);
            return;
        }

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

        AddHtml(startTag, ref state);
        AddText(markup.StyleToken, ref state);
        Visit(markup.Content, ref state);
        AddText(markup.StyleToken, ref state);
        AddHtml(endTag, ref state);
    }

    protected override void VisitUrl(UrlMarkup markup, ref StringBuilder state)
    {
        AddHtml("<a", ref state);
        AddAttribute("class", UrlClass, false, ref state);
        AddAttribute("target", "_blank", false, ref state);
        AddAttribute("href", markup.Url, ref state);
        AddHtml(">", ref state);
        AddText(markup.Url, ref state);
        AddHtml("</a>", ref state);
    }

    protected override void VisitMention(MentionMarkup markup, ref StringBuilder state)
    {
        AddHtml("<span", ref state);
        AddAttribute("class", MentionClass, false, ref state);
        AddAttribute("data-id", markup.Id.Value, true, ref state);
        AddHtml(">@", ref state);
        AddText(markup.NameOrNotAvailable, ref state);
        AddHtml("</span>", ref state);
    }

    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref StringBuilder state)
    {
        AddHtml("<div", ref state);
        AddAttribute("class", CodeBlockClass, false, ref state);
        AddAttribute("data-language", markup.Language, true, ref state);
        AddHtml(">", ref state);
        AddText(markup.Code, ref state);
        AddHtml("</div>", ref state);
    }

    protected override void VisitHeader(HeaderMarkup markup, ref StringBuilder state)
    {
        var tag = "h" + markup.Level;
        AddHtml("<", ref state);
        AddHtml(tag, ref state);
        AddAttribute("class", HeaderClass + " " + HeaderClass + "-" + markup.Level, false, ref state);
        AddHtml(">", ref state);
        Visit(markup.Content, ref state);
        AddHtml("</", ref state);
        AddHtml(tag, ref state);
        AddHtml(">", ref state);
    }

    protected override void VisitBlockQuote(BlockQuoteMarkup markup, ref StringBuilder state)
    {
        AddHtml("<blockquote", ref state);
        AddAttribute("class", "quote-markup", false, ref state);
        AddHtml(">", ref state);
        Visit(markup.Content, ref state);
        AddHtml("</blockquote>", ref state);
    }

    protected override void VisitTable(TableMarkup markup, ref StringBuilder state)
    {
        AddHtml("<table", ref state);
        AddAttribute("class", TableClass, false, ref state);
        AddHtml("><thead>", ref state);
        AddTableRow(markup.Header, markup.Alignments, "th", ref state);
        AddHtml("</thead><tbody>", ref state);
        foreach (var row in markup.Rows)
            AddTableRow(row, markup.Alignments, "td", ref state);
        AddHtml("</tbody></table>", ref state);
    }

    protected override void VisitTableRow(TableRowMarkup markup, ref StringBuilder state)
        => AddTableRow(markup, null, "td", ref state);

    protected override void VisitTableCell(TableCellMarkup markup, ref StringBuilder state)
        => Visit(markup.Content, ref state);

    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref StringBuilder state)
        => AddTextSpan(markup.Text, PreformattedTextClass, ref state);

    protected override void VisitPlainText(PlainTextMarkup markup, ref StringBuilder state)
        => AddText(markup.Text, ref state);

    protected override void VisitPlayableText(PlayableTextMarkup markup, ref StringBuilder state)
        => AddText(markup.Text, ref state);

    protected override void VisitNewLine(NewLineMarkup markup, ref StringBuilder state)
        => AddHtml(NewLineHtml, ref state);

    protected override void VisitUnparsed(UnparsedTextMarkup markup, ref StringBuilder state)
        => AddText(markup.Format(), ref state);

    protected override void VisitHashtag(HashtagMarkup markup, ref StringBuilder state)
        => AddText(markup.Text, ref state);

    protected override void VisitUnknown(Markup markup, ref StringBuilder state)
        => AddText(markup.Format(), ref state);

    // Protected methods

    protected void AddMarkup(Markup markup, ref StringBuilder state)
        => AddText(markup.Format(), ref state);

    protected void AddTableRow(
        TableRowMarkup row,
        TableColumnAlignment[]? alignments,
        string cellTag,
        ref StringBuilder state)
    {
        AddHtml("<tr>", ref state);
        for (var i = 0; i < row.Cells.Length; i++) {
            AddHtml("<", ref state);
            AddHtml(cellTag, ref state);
            if (alignments?[i].ToTextAlignStyle() is { } textAlign)
                AddAttribute("style", textAlign, false, ref state);
            AddHtml(">", ref state);
            VisitTableCell(row.Cells[i], ref state);
            AddHtml("</", ref state);
            AddHtml(cellTag, ref state);
            AddHtml(">", ref state);
        }
        AddHtml("</tr>", ref state);
    }

    protected void AddTextSpan(string text, string @class, ref StringBuilder state)
    {
        AddHtml("<span", ref state);
        AddAttribute("class", @class, false, ref state);
        AddHtml(">", ref state);
        AddText(text, ref state);
        AddHtml("</span>", ref state);
    }

    protected void AddText(string text, ref StringBuilder state)
    {
        if (NewLineReplacement != null) {
            // Normalize line endings to "\n" before HtmlEncode. HtmlEncoder.Default
            // emits numeric entities for both \r and \n (e.g. "\r\n" → "&#xD;&#xA;"),
            // which the regex form of NewLineRegex (\r?\n) can't match — so without
            // pre-normalization any \r leaks through and the browser ends up with a
            // stray \r in the decoded DOM text.
            text = NewLineRegex.Replace(text, "\n");
        }
        var html = text.HtmlEncode();
        if (NewLineReplacement != null && NewLineReplacement != "\n") {
            // HtmlEncode produced "&#xA;" for every \n; swap them for the desired HTML
            // (e.g. "<br/>") in one pass.
            html = html.Replace("&#xA;", NewLineReplacement);
        }
        AddHtml(html, ref state);
    }

#pragma warning disable CA1822
    protected void AddHtml(string html, ref StringBuilder state)
#pragma warning restore CA1822
        => state.Append(html);

    protected void AddAttribute(string name, string value, ref StringBuilder state)
        => AddAttribute(name, value, true, ref state);

#pragma warning disable CA1822
    protected void AddAttribute(string name, string value, bool mustEncode, ref StringBuilder state)
#pragma warning restore CA1822
    {
        state.Append(' ');
        state.Append(name);
        state.Append("=\"");
        state.Append(mustEncode ? value.HtmlEncode() : value);
        state.Append('"');
    }
}
