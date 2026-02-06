namespace ActualChat.Users;

/// <summary>
/// Extension methods for working with user identity collections.
/// </summary>
public static class UserIdentityExt
{
    public static UserIdentity GetIdentity(this ApiMap<UserIdentity, string> identities, string schema)
        => identities.FirstOrDefault(x => OrdinalEquals(x.Key.Schema, schema)).Key;

    public static List<UserIdentity> GetIdentities(this ApiMap<UserIdentity, string> identities, string schema)
        => identities.Where(x => OrdinalEquals(x.Key.Schema, schema)).Select(x => x.Key).ToList();

    // Internal identity (for special users defined in Constants)

    public static bool HasInternalIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.HasInternalIdentity(out _);

    public static bool HasInternalIdentity(this ApiMap<UserIdentity, string> identities, [NotNullWhen(true)] out UserId? userId)
    {
        var identity = identities.Keys.FirstOrDefault(x => OrdinalEquals(x.Schema, UserIdentity.InternalSchema));
        userId = identity.IsValid ? UserId.TryParse(identity.Value) : null;
        return userId is not null;
    }

    public static ApiMap<UserIdentity, string> WithInternalIdentity(
        this ApiMap<UserIdentity, string> identities, UserId userId)
        => identities.With(new UserIdentity(UserIdentity.InternalSchema, userId.Require().Value), "");

    // Google identity

    public static bool HasGoogleIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.HasGoogleIdentity(out _);

    public static bool HasGoogleIdentity(this ApiMap<UserIdentity, string> identities, out UserIdentity googleIdentity)
    {
        googleIdentity = identities.Keys.FirstOrDefault(x => OrdinalEquals(x.Schema, AuthSchema.Google));
        return googleIdentity.IsValid;
    }

    public static ApiMap<UserIdentity, string> WithGoogleIdentity(
        this ApiMap<UserIdentity, string> identities, string googleId)
        => identities.With(new UserIdentity(AuthSchema.Google, googleId.RequireNonEmpty()), "");

    // Apple identity

    public static bool HasAppleIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.HasAppleIdentity(out _);

    public static bool HasAppleIdentity(this ApiMap<UserIdentity, string> identities, out UserIdentity appleIdentity)
    {
        appleIdentity = identities.Keys.FirstOrDefault(x => OrdinalEquals(x.Schema, AuthSchema.Apple));
        return appleIdentity.IsValid;
    }

    public static ApiMap<UserIdentity, string> WithAppleIdentity(
        this ApiMap<UserIdentity, string> identities, string appleId)
        => identities.With(new UserIdentity(AuthSchema.Apple, appleId.RequireNonEmpty()), "");

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
