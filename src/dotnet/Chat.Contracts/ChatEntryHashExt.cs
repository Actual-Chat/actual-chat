using ActualChat.Hashing;

namespace ActualChat.Chat;

public static class ChatEntryHashExt
{
    public static HashString GetContentHashString(this ChatEntry entry)
        => GetContentHashString(entry.Content);

    public static HashString GetContentHashString(string content)
        => content.Hash().Blake3().ToBlake3Base64HashString();
}
