using ActualChat.Transcription;

namespace ActualChat.Chat;

public static class TranslationExt
{
    public static LinearMap GetTimeMap(this Translation translation, ChatEntry entry)
        => entry.TimeMap.Scale(entry.Content.Length, translation.Content.Length);
}
