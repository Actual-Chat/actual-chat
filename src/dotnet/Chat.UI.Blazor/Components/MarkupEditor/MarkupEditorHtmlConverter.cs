using ActualChat.Chat.UI.Blazor.Services;
using Cysharp.Text;

namespace ActualChat.Chat.UI.Blazor.Components;

public sealed record MarkupEditorHtmlConverter : MarkupHtmlFormatterBase
{
    public MarkupHub MarkupHub { get; init; }

    public MarkupEditorHtmlConverter(MarkupHub markupHub)
    {
        MarkupHub = markupHub;
        UrlClass = "editor-url";
        MentionClass = "editor-mention";
        PreformattedTextClass = "editor-preformatted";
        CodeBlockClass = "editor-preformatted";
        NewLineHtml = "\n";
        NewLineReplacement = "\n";
    }

    public async Task<string> Convert(string markupText, CancellationToken cancellationToken)
    {
        var markup = MarkupHub.MarkupParser.Parse(markupText);
        markup = await MarkupHub.MentionNamer.Rewrite(markup, cancellationToken).ConfigureAwait(false);
        return Format(markup);
    }

    protected override void VisitMention(MentionMarkup markup, ref Utf16ValueStringBuilder state)
    {
        AddHtml("<span", ref state);
        AddAttribute("class", MentionClass, false, ref state);
        AddAttribute("contenteditable", "false", false, ref state);
        AddAttribute("data-id", markup.Id, true, ref state);
        AddHtml(">@", ref state);
        AddHiddenText("`", ref state);
        AddText(markup.NameOrNotAvailable, ref state);
        AddHiddenText("`" + markup.Id, ref state);
        AddHtml("</span>&#8203", ref state);
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
