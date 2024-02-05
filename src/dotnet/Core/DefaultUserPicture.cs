using ActualChat.Hashing;

namespace ActualChat;

public static class DefaultUserPicture
{
    public const string BoringAvatarsBaseUrl = "https://source.boringavatars.com/";

    public static string GetAvatarKey(string key)
        // => key.Hash(Encoding.UTF8).SHA1().AlphaNumeric();
        => key.Hash().Blake2s().Base16(16);
}
