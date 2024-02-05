using System.Text;
using ActualChat.Contacts;
using ActualChat.Hashing;

namespace ActualChat.Testing;

public static class ExternalContactExt
{
    public static ExternalContact WithoutPhone(this ExternalContact externalContact, Phone phone)
        => externalContact with { PhoneHashes = externalContact.PhoneHashes.Without(Hash(phone.Value)) };

    public static ExternalContact WithPhone(this ExternalContact externalContact, Phone phone)
        => externalContact with { PhoneHashes = externalContact.PhoneHashes.With(Hash(phone.Value)) };

    public static ExternalContact WithoutEmail(this ExternalContact externalContact, string email)
        => externalContact with { EmailHashes = externalContact.EmailHashes.Without(Hash(email)) };

    public static ExternalContact WithEmail(this ExternalContact externalContact, string email)
        => externalContact with { EmailHashes = externalContact.EmailHashes.With(Hash(email)) };

    // Private methods

    private static string Hash(string value)
        => value.Hash(Encoding.UTF8).SHA256().Base64();
}
