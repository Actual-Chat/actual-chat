using ActualChat.Hashing;

namespace ActualChat.Contacts;

public static class ExternalContactExt
{
    public static ExternalContactFull WithoutPhone(this ExternalContactFull externalContact, Phone phone)
        => externalContact with { PhoneHashes = externalContact.PhoneHashes.Without(phone.Hash) };

    public static ExternalContactFull WithPhone(this ExternalContactFull externalContact, Phone phone)
        => externalContact with { PhoneHashes = externalContact.PhoneHashes.With(phone.Hash) };

    public static ExternalContactFull WithoutEmail(this ExternalContactFull externalContact, string email)
        => externalContact with { EmailHashes = externalContact.EmailHashes.Without(ContactExt.Hash(email)) };

    public static ExternalContactFull WithEmail(this ExternalContactFull externalContact, string email)
        => externalContact with { EmailHashes = externalContact.EmailHashes.With(ContactExt.Hash(email)) };

    public static ExternalContactFull WithHash(this ExternalContactFull externalContact, ExternalContactHasher hasher, bool recompute = true)
        => recompute || externalContact.Hash.IsNone || externalContact.Hash.Algorithm != HashAlgorithm.SHA256
            ? externalContact with { Hash = hasher.Compute(externalContact) }
            : externalContact;

    public static ExternalContactFull WithDisplayName(this ExternalContactFull externalContact, string displayName)
        => externalContact with { DisplayName = displayName };
}
