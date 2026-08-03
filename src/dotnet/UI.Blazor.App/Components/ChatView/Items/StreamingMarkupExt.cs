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

        var markup = Parser.Parse(streamedText);
        return markup.IsPlainText() ? null : markup;
    }
}
