namespace ActualChat.Chat;

public static class ChatEntryExt
{
    public static bool NeedsTranslate([NotNullWhen(true)] this ChatEntry? entry, Translation? translation)
    {
        if (!entry.SupportsTranslation())
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

    public static bool SupportsLanguageDetection([NotNullWhen(true)] this ChatEntry? entry)
    {
        if (!entry.SupportsTranslation())
            return false;

        // languages are already saved for transcribed messages
        return entry is { HasAudioEntry: false, HasVideoEntry: false };
    }

    private static bool SupportsTranslation([NotNullWhen(true)] this ChatEntry? entry)
    {
        if (entry is null)
            return false;

        if (entry.IsSystemEntry || entry.Kind != ChatEntryKind.Text)
            return false;

        return !entry.Content.IsNullOrEmpty() && entry.Content.Any(char.IsLetter);
    }
}
