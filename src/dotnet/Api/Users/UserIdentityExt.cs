namespace ActualChat.Users;

public static class UserIdentityExt
{
    public static UserIdentity GetIdentity(this ApiMap<UserIdentity, string> identities, string schema)
        => identities.FirstOrDefault(x => OrdinalEquals(x.Key.Schema, schema)).Key;

    public static List<UserIdentity> GetIdentities(this ApiMap<UserIdentity, string> identities, string schema)
        => identities.Where(x => OrdinalEquals(x.Key.Schema, schema)).Select(x => x.Key).ToList();

    // Email identities

    public static List<UserIdentity> GetEmailIdentities(this ApiMap<UserIdentity, string> identities)
        => identities.GetIdentities(AuthSchema.Email);

    public static List<UserIdentity> GetHashedEmailIdentities(this ApiMap<UserIdentity, string> identities)
        => identities.GetIdentities(AuthSchema.HashedEmail);

    public static List<string> GetEmails(this ApiMap<UserIdentity, string> identities)
        => identities.GetEmailIdentities().Where(x => !x.Value.IsNullOrEmpty()).Select(x => x.Value).ToList();

    public static List<string> GetEmailHashes(this ApiMap<UserIdentity, string> identities)
        => identities.GetHashedEmailIdentities().Select(x => x.Value).ToList();

    public static bool HasEmail(this ApiMap<UserIdentity, string> identities, string email)
        => identities.GetEmails().Any(x => OrdinalEquals(x, email));

    public static ApiMap<UserIdentity, string> WithEmailIdentity(
        this ApiMap<UserIdentity, string> identities, Email email)
        => identities.WithEmailIdentity(email, out _);

    public static ApiMap<UserIdentity, string> WithEmailIdentity(
        this ApiMap<UserIdentity, string> identities, Email email, out UserIdentity emailIdentity)
    {
        emailIdentity = NewEmailIdentity(email);
        var hashedEmailIdentity = NewHashedEmailIdentity(email.Hash);
        return identities.With(emailIdentity, "").With(hashedEmailIdentity, "");
    }

    // Phone identities

    public static List<UserIdentity> GetPhoneIdentities(this ApiMap<UserIdentity, string> identities)
        => identities.GetIdentities(AuthSchema.Phone);

    public static List<UserIdentity> GetHashedPhoneIdentities(this ApiMap<UserIdentity, string> identities)
        => identities.GetIdentities(AuthSchema.HashedPhone);

    public static List<Phone> GetPhones(this ApiMap<UserIdentity, string> identities)
        => identities.GetPhoneIdentities().Select(x => Phone.TryParse(x.Value)).SkipNullItems().ToList();

    public static List<string> GetPhoneHashes(this ApiMap<UserIdentity, string> identities)
        => identities.GetHashedPhoneIdentities().Select(x => x.Value).ToList();

    public static bool HasPhone(this ApiMap<UserIdentity, string> identities, Phone phone)
        => identities.GetPhones().Any(x => x == phone);

    public static ApiMap<UserIdentity, string> WithPhoneIdentity(
        this ApiMap<UserIdentity, string> identities, Phone phone)
        => identities.WithPhoneIdentity(phone, out _);

    public static ApiMap<UserIdentity, string> WithPhoneIdentity(
        this ApiMap<UserIdentity, string> identities, Phone phone, out UserIdentity phoneIdentity)
    {
        phoneIdentity = NewPhoneIdentity(phone);
        var hashedPhoneIdentity = NewHashedPhoneIdentity(phone.Hash);
        return identities.With(phoneIdentity, "").With(hashedPhoneIdentity, "");
    }

    // Factory methods for creating identities

    public static UserIdentity NewEmailIdentity(Email email)
        => new(AuthSchema.Email, email.Value);

    public static UserIdentity NewPhoneIdentity(Phone phone)
        => new(AuthSchema.Phone, phone.Value);

    public static UserIdentity NewHashedEmailIdentity(string emailHash)
        => new(AuthSchema.HashedEmail, emailHash);

    public static UserIdentity NewHashedPhoneIdentity(string phoneHash)
        => new(AuthSchema.HashedPhone, phoneHash);
}
