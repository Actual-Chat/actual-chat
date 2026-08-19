using System.Text;

namespace ActualChat.Chat;

public record MarkupEditorHtmlConverter : MarkupHtmlFormatterBase
{
    public static readonly MarkupEditorHtmlConverter Instance = new();

    public MarkupEditorHtmlConverter()
    {
        UrlClass = "editor-url";
        MentionClass = "editor-mention";
        PreformattedTextClass = "editor-preformatted";
        CodeBlockClass = "editor-preformatted";
        NewLineHtml = NewLineReplacement = "\n";
    }

    protected override void VisitMention(MentionMarkup markup, ref StringBuilder state)
    {
        var kindClass = markup switch {
            EmojiMention => MentionClass + " " + MentionClass + "-emoji",
            ChatMention => MentionClass + " " + MentionClass + "-chat",
            UserMention => MentionClass + " " + MentionClass + "-user",
            AuthorMention => MentionClass + " " + MentionClass + "-user",
            PlaceMention => MentionClass + " " + MentionClass + "-chat",
            _ => MentionClass,
        };

        AddHtml("<span", ref state);
        AddAttribute("class", kindClass, false, ref state);
        AddAttribute("contenteditable", "false", false, ref state);
        AddAttribute("data-content-editable", "false", false, ref state);
        AddAttribute("data-id", markup.Id.Value, true, ref state);
        AddHtml(">", ref state);

        if (markup is EmojiMention emoji)
            VisitEmojiBody(emoji, ref state);
        else {
            AddHiddenText("@`", ref state);
            AddText(markup.NameOrNotAvailable, ref state);
            AddHiddenText("`" + markup.Id, ref state);
        }
        AddHtml("&#8203</span>&#8203", ref state);
    }

    protected override void VisitStylized(StylizedMarkup markup, ref StringBuilder state)
        => AddText(markup.Format(), ref state);

    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref StringBuilder state)
        => AddText(markup.Format(), ref state);

    protected override void VisitHeader(HeaderMarkup markup, ref StringBuilder state)
        => AddText(markup.Format(), ref state);

    protected override void VisitBlockQuote(BlockQuoteMarkup markup, ref StringBuilder state)
        => AddText(markup.Format(), ref state);

    protected override void VisitTable(TableMarkup markup, ref StringBuilder state)
        => AddText(markup.Format(), ref state);

    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref StringBuilder state)
        => AddText(markup.Format(), ref state);

    // Private methods

    private void VisitEmojiBody(EmojiMention emoji, ref StringBuilder state)
    {
        var resolved = Emojis.TryGetByIdOrSymbol(emoji.EmojiRef.Text);
        var glyph = emoji.Glyph.NullIfEmpty() ?? resolved?.Symbol;
        // Prefer the unicode glyph as the "name" in the round-trip text so copy-paste
        // produces @`😊`e:<id> rather than @`Smiling face`e:<id>.
        var roundTripName = glyph.NullIfEmpty() ?? emoji.NameOrNotAvailable;
        AddHiddenText("@`" + roundTripName + "`" + emoji.Id, ref state);

        var svgName = Emojis.TryGetSvgName(resolved);
        if (svgName != null) {
            AddHtml("<img class=\"editor-emoji\" src=\"/dist/images/emoji/", ref state);
            AddHtml(svgName, ref state);
            AddHtml(".svg\" alt=\"\" draggable=\"false\" />", ref state);
            return;
        }

        if (!glyph.IsNullOrEmpty()) {
            AddText(glyph, ref state);
            return;
        }

        AddText(":" + emoji.EmojiRef.Text + ":", ref state);
    }

    private static void AddHiddenText(string text, ref StringBuilder state)
    {
        state.Append("<span class=\"editor-hidden\">");
        state.Append(text.HtmlEncode());
        state.Append("</span>");
    }
}