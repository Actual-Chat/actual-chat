using System.Text;
using ActualChat.Hashing;

namespace ActualChat.Contacts;

public static class ExternalContactExt
{
    public static ExternalContactFull WithoutPhone(this ExternalContactFull externalContact, Phone phone)
        => externalContact with { PhoneHashes = externalContact.PhoneHashes.Without(Hash(phone.Value)) };

    public static ExternalContactFull WithPhone(this ExternalContactFull externalContact, Phone phone)
        => externalContact with { PhoneHashes = externalContact.PhoneHashes.With(Hash(phone.Value)) };

    public static ExternalContactFull WithoutEmail(this ExternalContactFull externalContact, string email)
        => externalContact with { EmailHashes = externalContact.EmailHashes.Without(Hash(email)) };

    public static ExternalContactFull WithEmail(this ExternalContactFull externalContact, string email)
        => externalContact with { EmailHashes = externalContact.EmailHashes.With(Hash(email)) };

    public static ExternalContactFull WithHash(this ExternalContactFull externalContact, ExternalContactHasher hasher, bool recompute = true)
        => recompute || externalContact.Sha256Hash.IsNullOrEmpty()
            ? externalContact with { Sha256Hash = hasher.Compute(externalContact) }
            : externalContact;

    // Private methods

    private static string Hash(string value)
        => value.Hash(Encoding.UTF8).SHA256().Base64();
}
