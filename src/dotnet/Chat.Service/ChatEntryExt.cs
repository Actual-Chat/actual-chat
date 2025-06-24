namespace ActualChat.Chat;

public static class ChatEntryExt
{
    public static bool NeedsTranslation([NotNullWhen(true)] this ChatEntry? entry, Translation? translation)
    {
        if (!entry.SupportsTranslation(false))
            return false;

        if (entry.IsRemoved)
            return false;

        if (translation is null)
            return true;

        if (translation.IsStreaming)
            return false;

        return translation.SourceContentHash != entry.ContentHash;
    }

    public static bool NeedsLanguageDetection([NotNullWhen(true)] this ChatEntry? entry, ChatEntryLanguage? entryLanguage)
    {
        if (!entry.SupportsLanguageDetection())
            return false;

        if (entry.IsRemoved)
            return false;

        if (entryLanguage is null)
            return true;

        return entryLanguage.EntryContentHash != entry.ContentHash;
    }
}
