using ActualChat.Chat;

namespace ActualChat.UI.Blazor.App.Components;

public static class StreamingMarkupExt
{
    private static readonly IMarkupParser Parser = new MarkupParser { AllowIncompleteMarkup = true };

    public static Markup? ParseOrNullIfPlainText(string streamedText)
    {
        // Null means "nothing markup-shaped arrived yet", which lets the caller keep the
        // per-character animation it uses for transcripts.
        if (streamedText.IsNullOrEmpty())
            return null;

        // Plain text keeps the raw view only if the parse left it as it came. A reset, a half-arrived
        // marker or a half-arrived link hides part of it, and the raw view would show that part.
        // Blank lines around the text don't count: the parse drops the leading ones.
        var markup = Parser.Parse(streamedText);
        if (!markup.IsPlainText())
            return markup;

        var normalizedText = streamedText.NormalizeNewLines(NewLineMarkup.Instance.Text);
        return markup.Format().Trim() == normalizedText.Trim() ? null : markup;
    }
}
