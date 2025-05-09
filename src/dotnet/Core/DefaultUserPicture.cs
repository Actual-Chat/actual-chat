using ActualChat.Hashing;

namespace ActualChat;

public static class DefaultUserPicture
{
    public static string GetAvatarKey(string key)
        => key.Hash().Blake2s().Base16(16);
}
