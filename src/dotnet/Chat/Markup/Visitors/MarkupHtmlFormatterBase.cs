using System.Text.RegularExpressions;
using Cysharp.Text;

namespace ActualChat.Chat;

public abstract partial record MarkupHtmlFormatterBase : MarkupFormatterBase
{
    [GeneratedRegex(@"\r?\n")]
    private static partial Regex NewLineRegexFactory();

    private static readonly Regex NewLineRegex = NewLineRegexFactory();

    public string UrlClass { get; init; } = "markup-url";
    public string MentionClass { get; init; } = "markup-mention";
    public string CodeBlockClass { get; init; } = "markup-code";
    public string PreformattedTextClass { get; init; } = "markup-preformatted-text";
    public string NewLineHtml { get; init; } = "<br/>";
    public string? NewLineReplacement { get; init; } = null;

    protected override void VisitStylized(StylizedMarkup markup, ref Utf16ValueStringBuilder state)
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

        AddHtml(startTag, ref state);
        AddText(markup.StyleToken, ref state);
        Visit(markup.Content, ref state);
        AddText(markup.StyleToken, ref state);
        AddHtml(endTag, ref state);
    }

    protected override void VisitUrl(UrlMarkup markup, ref Utf16ValueStringBuilder state)
    {
        AddHtml("<a", ref state);
        AddAttribute("class", UrlClass, false, ref state);
        AddAttribute("target", "_blank", false, ref state);
        AddAttribute("href", markup.Url, ref state);
        AddHtml(">", ref state);
        AddText(markup.Url, ref state);
        AddHtml("</a>", ref state);
    }

    protected override void VisitMention(MentionMarkup markup, ref Utf16ValueStringBuilder state)
    {
        AddHtml("<span", ref state);
        AddAttribute("class", MentionClass, false, ref state);
        AddAttribute("data-id", markup.Id, true, ref state);
        AddHtml(">@", ref state);
        AddText(markup.NameOrNotAvailable, ref state);
        AddHtml("</span>", ref state);
    }

    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref Utf16ValueStringBuilder state)
    {
        AddHtml("<div", ref state);
        AddAttribute("class", CodeBlockClass, false, ref state);
        AddAttribute("data-language", markup.Language, true, ref state);
        AddHtml(">", ref state);
        AddText(markup.Code, ref state);
        AddHtml("</div>", ref state);
    }

    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref Utf16ValueStringBuilder state)
        => AddTextSpan(markup.Text, PreformattedTextClass, ref state);

    protected override void VisitPlainText(PlainTextMarkup markup, ref Utf16ValueStringBuilder state)
        => AddText(markup.Text, ref state);

    protected override void VisitPlayableText(PlayableTextMarkup markup, ref Utf16ValueStringBuilder state)
        => AddText(markup.Text, ref state);

    protected override void VisitNewLine(NewLineMarkup markup, ref Utf16ValueStringBuilder state)
        => AddHtml(NewLineHtml, ref state);

    protected override void VisitUnparsed(UnparsedTextMarkup markup, ref Utf16ValueStringBuilder state)
        => AddText(markup.Format(), ref state);

    protected override void VisitUnknown(Markup markup, ref Utf16ValueStringBuilder state)
        => AddText(markup.Format(), ref state);

    // Protected methods

    protected void AddMarkup(Markup markup, ref Utf16ValueStringBuilder state)
        => AddText(markup.Format(), ref state);

    protected void AddTextSpan(string text, string @class, ref Utf16ValueStringBuilder state)
    {
        AddHtml("<span", ref state);
        AddAttribute("class", @class, false, ref state);
        AddHtml(">", ref state);
        AddText(text, ref state);
        AddHtml("</span>", ref state);
    }

    protected void AddText(string text, ref Utf16ValueStringBuilder state)
    {
        var html = text.HtmlEncode();
        if (NewLineReplacement != null)
            html = NewLineRegex.Replace(html, NewLineReplacement);
        AddHtml(html, ref state);
    }

    protected void AddHtml(string html, ref Utf16ValueStringBuilder state)
        => state.Append(html);

    protected void AddAttribute(string name, string value, ref Utf16ValueStringBuilder state)
        => AddAttribute(name, value, true, ref state);

    protected void AddAttribute(string name, string value, bool mustEncode, ref Utf16ValueStringBuilder state)
    {
        state.Append(" ");
        state.Append(name);
        state.Append("=\"");
        state.Append(mustEncode ? value.HtmlEncode() : value);
        state.Append("\"");
    }
}
