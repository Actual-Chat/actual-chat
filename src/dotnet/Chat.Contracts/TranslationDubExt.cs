using ActualChat.Hashing;

namespace ActualChat.Chat;

public static class TranslationDubExt
{
    public static bool HasValidDub(this Translation translation, string voiceId = "")
        => translation.DubMediaId != null
            && translation.DubContentHash == GetDubContentHash(translation.Content, voiceId);

    // The default voice hashes the content alone, so dubs stored before voices existed stay valid
    public static HashString GetDubContentHash(string content, string voiceId = "")
        => ChatEntryHashExt.GetContentHashString(voiceId.IsNullOrEmpty() ? content : $"{content}\n{voiceId}");
}
