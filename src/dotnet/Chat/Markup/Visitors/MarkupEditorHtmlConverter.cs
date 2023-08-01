using Cysharp.Text;

namespace ActualChat.Chat;

public record MarkupEditorHtmlConverter : MarkupHtmlFormatterBase
{
    public static MarkupEditorHtmlConverter Instance { get; } = new();

    public MarkupEditorHtmlConverter()
    {
        UrlClass = "editor-url";
        MentionClass = "editor-mention";
        PreformattedTextClass = "editor-preformatted";
        CodeBlockClass = "editor-preformatted";
        NewLineHtml = NewLineReplacement = "\n";
    }

    protected override void VisitMention(MentionMarkup markup, ref Utf16ValueStringBuilder state)
    {
        AddHtml("<span", ref state);
        AddAttribute("class", MentionClass, false, ref state);
        AddAttribute("contenteditable", "false", false, ref state);
        AddAttribute("data-content-editable", "false", false, ref state);
        AddAttribute("data-id", markup.Id, true, ref state);
        AddHtml(">@", ref state);
        AddHiddenText("`", ref state);
        AddText(markup.NameOrNotAvailable, ref state);
        AddHiddenText("`" + markup.Id, ref state);
        AddHtml("&#8203</span>&#8203", ref state);
    }

    protected override void VisitStylized(StylizedMarkup markup, ref Utf16ValueStringBuilder state)
        => AddText(markup.Format(), ref state);

    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref Utf16ValueStringBuilder state)
        => AddText(markup.Format(), ref state);

    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref Utf16ValueStringBuilder state)
        => AddText(markup.Format(), ref state);

    // Private methods

    private void AddHiddenText(string text, ref Utf16ValueStringBuilder state)
    {
        state.Append("<span class=\"editor-hidden\">");
        state.Append(text.HtmlEncode());
        state.Append("</span>");
    }
}
