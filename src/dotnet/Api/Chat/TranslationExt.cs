using ActualChat.Transcription;

namespace ActualChat.Chat;

public static class TranslationExt
{
    public static LinearMap GetTimeMap(this Translation translation, ChatEntry entry)
        => (entry.Audio?.TimeMap ?? default).Scale(entry.Content.Length, translation.Content.Length);

    public static bool ContentSupportsTranslation(string content)
        => !content.IsNullOrEmpty() && content.Any(char.IsLetter);
}
