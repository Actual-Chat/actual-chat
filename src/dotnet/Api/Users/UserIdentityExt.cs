namespace ActualChat.Users;

public static class UserIdentityExt
{
    // GetXxxIdentity

    public static UserIdentity GetIdentity(this ApiMap<UserIdentity, string> identities, string schema)
        => identities.FirstOrDefault(x => OrdinalEquals(x.Key.Schema, schema)).Key;

    public static UserIdentity GetPhoneIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.GetIdentity(AuthSchema.Phone);

    public static UserIdentity GetEmailIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.GetIdentity(AuthSchema.Email);

    public static UserIdentity GetHashedPhoneIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.GetIdentity(AuthSchema.HashedPhone);

    public static UserIdentity GetHashedEmailIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.GetIdentity(AuthSchema.HashedEmail);

    // GetXxx (extract values from identities)

    public static string? GetEmail(this ApiMap<UserIdentity, string> identities)
        => identities.GetEmailIdentity().SchemaBoundId.NullIfEmpty();

    public static Phone? GetPhone(this ApiMap<UserIdentity, string> identities)
        => Phone.TryParse(identities.GetPhoneIdentity().SchemaBoundId);

    public static string? GetPhoneHash(this ApiMap<UserIdentity, string> identities)
        => identities.GetHashedPhoneIdentity().SchemaBoundId.NullIfEmpty();

    public static string? GetEmailHash(this ApiMap<UserIdentity, string> identities)
        => identities.GetHashedEmailIdentity().SchemaBoundId.NullIfEmpty();

    // Factory methods for creating identities

    public static UserIdentity NewPhoneIdentity(Phone phone)
        => new(AuthSchema.Phone, phone.Value);

    public static UserIdentity NewEmailIdentity(Email email)
        => new(AuthSchema.Email, email.Value);

    public static UserIdentity NewHashedPhoneIdentity(string phoneHash)
        => new(AuthSchema.HashedPhone, phoneHash);

    public static UserIdentity NewHashedEmailIdentity(string emailHash)
        => new(AuthSchema.HashedEmail, emailHash);
}
