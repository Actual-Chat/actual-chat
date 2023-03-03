namespace ActualChat;

public static class DefaultUserPicture
{
    public const int DefaultSize = 160;

    public static string Get(string name, int size = DefaultSize)
        => GetBoringAvatar(name.GetSHA1HashCode(), size);

    public static string GetBoringAvatar(string hash, int size = DefaultSize)
        => $"https://source.boringavatars.com/beam/{size}/{hash.UrlEncode()}?colors=FFDBA0,BBBEFF,9294E1,FF9BC0,0F2FE8";
}
