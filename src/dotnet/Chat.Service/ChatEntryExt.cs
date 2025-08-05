namespace ActualChat.Chat;

public static class ChatEntryExt
{
    public static bool NeedsTranslation([NotNullWhen(true)] this TranslationSource? source, Translation? translation, bool isRetranslation = false)
    {
        if (source is null)
            return false;

        if (source is TextEntryTranslationSource textEntrySource) {
            var entry = textEntrySource.ChatEntry;
            if (!entry.SupportsTranslation(false))
                return false;
            if (entry.IsRemoved)
                return false;
        }

        if (!TranslationExt.ContentSupportsTranslation(source.Content))
            return false;

        if (translation is null)
            return true;

        if (translation.IsStreaming)
            return false;

        return translation.SourceContentHash != source.ContentHash && !isRetranslation;
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
