namespace ActualChat.UI.Blazor.App.Components;

public enum TranslationState {
    None,
    Translating,
    Translated,
}

public record TranslatedText(string Text, TranslationState State)
{
    public static readonly TranslatedText None = From("");

    public static TranslatedText From(string originalText)
        => new (originalText, TranslationState.None);

    public static TranslatedText From(string originalText, Translation? translation)
    {
        if (translation is null)
            return new TranslatedText(originalText, TranslationState.Translating);

        if (translation.MatchesOriginal(originalText))
            return new TranslatedText(originalText, TranslationState.None);

        return new TranslatedText(translation.Content, TranslationState.Translated);
    }
}

public record TranslatedMarkup(Markup Markup, TranslationState State)
{
    public static readonly TranslatedMarkup None = From(Markup.Empty);

    public static TranslatedMarkup From(Markup originalMarkup)
        => new (originalMarkup, TranslationState.None);

    public static TranslatedMarkup From(string originalText, Translation? translation, IChatMarkupHub markupHub, Markup originalMarkup)
    {
        if (translation is null)
            return new TranslatedMarkup(originalMarkup, TranslationState.Translating);

        if (translation.MatchesOriginal(originalText))
            return new TranslatedMarkup(originalMarkup, TranslationState.None);

        var markup = markupHub.Parser.Parse(translation.Content);
        return new TranslatedMarkup(markup, TranslationState.Translated);
    }
}
