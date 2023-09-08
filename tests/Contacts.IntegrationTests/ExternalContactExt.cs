namespace ActualChat.Contacts.IntegrationTests;

public static class ExternalContactExt
{
    public static ExternalContact WithoutPhone(this ExternalContact externalContact, Phone phone)
        => externalContact with { PhoneHashes = externalContact.PhoneHashes.Without(phone.Value.GetSHA256HashCode()) };

    public static ExternalContact WithPhone(this ExternalContact externalContact, Phone phone)
        => externalContact with { PhoneHashes = externalContact.PhoneHashes.With(phone.Value.GetSHA256HashCode()) };

    public static ExternalContact WithoutEmail(this ExternalContact externalContact, string email)
        => externalContact with { EmailHashes = externalContact.EmailHashes.Without(email.GetSHA256HashCode()) };

    public static ExternalContact WithEmail(this ExternalContact externalContact, string email)
        => externalContact with { EmailHashes = externalContact.EmailHashes.With(email.GetSHA256HashCode()) };
}
