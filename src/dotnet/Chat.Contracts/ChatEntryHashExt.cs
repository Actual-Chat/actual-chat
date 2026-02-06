using ActualChat.Hashing;

namespace ActualChat.Chat;

/// <summary>
/// Extension methods for computing chat entry content hashes.
/// </summary>
public static class ChatEntryHashExt
{
    public static HashString GetContentHashString(this ChatEntry entry)
        => GetContentHashString(entry.Content);

    public static HashString GetContentHashString(string content)
        => content.Hash().Blake3().ToBlake3Base64HashString();
}
