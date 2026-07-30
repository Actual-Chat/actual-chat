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

        // A re-translation is enqueued by the realtime translator itself, once its stream is over,
        // so a StreamId that's still set at that point means the realtime finalization was lost
        // (e.g. the translation stream failed). Letting it block the re-translation would leave the
        // entry with a dangling streaming translation until TranslationCleanupFlow drops it.
        if (translation.IsStreaming && !isRetranslation)
            return false;

        return translation.SourceContentHash != source.ContentHash || isRetranslation;
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
