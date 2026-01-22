namespace ActualChat.Users;

public static class UserIdentityExt
{
    // Core method to get identity by schema
    public static UserIdentity GetIdentity(this ApiMap<UserIdentity, string> identities, string schema)
        => identities.FirstOrDefault(x => OrdinalEquals(x.Key.Schema, schema)).Key;

    // Get specific identity types
    public static UserIdentity GetPhoneIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.GetIdentity(AuthSchema.Phone);

    public static UserIdentity GetEmailIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.GetIdentity(AuthSchema.Email);

    public static UserIdentity GetHashedPhoneIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.GetIdentity(AuthSchema.HashedPhone);

    public static UserIdentity GetHashedEmailIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.GetIdentity(AuthSchema.HashedEmail);

    // Check for identity presence
    public static bool HasPhoneIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.GetPhoneIdentity().IsValid;

    public static bool HasEmailIdentity(this ApiMap<UserIdentity, string> identities)
        => identities.GetEmailIdentity().IsValid;

    // Extract values from identities
    public static string? GetEmail(this ApiMap<UserIdentity, string> identities)
        => identities.GetEmailIdentity().SchemaBoundId.NullIfEmpty();

    public static Phone? GetPhone(this ApiMap<UserIdentity, string> identities)
        => Phone.TryParse(identities.GetPhoneIdentity().SchemaBoundId);

    public static string? GetPhoneHash(this ApiMap<UserIdentity, string> identities)
        => identities.GetHashedPhoneIdentity().SchemaBoundId.NullIfEmpty();

    public static string? GetEmailHash(this ApiMap<UserIdentity, string> identities)
        => identities.GetHashedEmailIdentity().SchemaBoundId.NullIfEmpty();

    // Factory methods for creating identities
    public static UserIdentity ToPhoneIdentity(Phone phone)
        => new(AuthSchema.Phone, phone.Value);

    public static UserIdentity ToEmailIdentity(Email email)
        => new(AuthSchema.Email, email.Value);

    public static UserIdentity ToHashedPhoneIdentity(string phoneHash)
        => new(AuthSchema.HashedPhone, phoneHash);

    public static UserIdentity ToHashedEmailIdentity(string emailHash)
        => new(AuthSchema.HashedEmail, emailHash);
}
