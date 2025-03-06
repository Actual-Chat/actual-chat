namespace ActualChat;

public static class LanguageExt
{
    public static bool IsEnglish(this Language language)
        => language.ShortTitle.Value.OrdinalStartsWith("en");
}
