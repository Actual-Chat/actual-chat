using System.Text;
using ActualChat.Hashing;

namespace ActualChat;

public static class ContactLinkExt
{
    public static string Hash(string value)
        => value.ToLowerInvariant().Hash(Encoding.UTF8).SHA256().Base64();
}
