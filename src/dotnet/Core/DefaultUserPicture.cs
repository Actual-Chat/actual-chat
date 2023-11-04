namespace ActualChat;

public static class DefaultUserPicture
{
    public const string BoringAvatarsBaseUrl = "https://source.boringavatars.com/";

    public static string GetAvatarKey(string name)
        => name.GetSHA1HashCode(HashEncoding.AlphaNumeric);
}
