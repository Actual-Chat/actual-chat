namespace ActualChat.Chat;

public static class ChatEntryLanguageExt
{
    public static bool IsEmpty([NotNullWhen(false)] this ChatEntryLanguage? entryLanguage)
        => entryLanguage is null || entryLanguage.Languages.All(x => x.IsNone);
}
