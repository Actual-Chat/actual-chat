namespace ActualChat;

public static class AuthSchema
{
    public const string Google = "Google";
    public const string Apple = "Apple";
    public const string Phone = "phone";
    public const string HashedPhone = "phone-hash";
    public const string Email = "email";
    public const string HashedEmail = "email-hash";
    public const string EmailVerifiedClaim = "email_verified";

    public static readonly string[] AllExternal = [Google, Apple];

    public static readonly IReadOnlyDictionary<string, string> DisplayNames
        = new Dictionary<string, string>() {
            [Google] = "Google",
            [Apple] = "Apple",
            [Phone] = "Phone",
            [HashedPhone] = "Phone",
            [Email] = "Email",
            [HashedEmail] = "Email",
        };
    public static readonly IReadOnlySet<string> ExternalSchemas
        = new HashSet<string>() { Google, Apple };

    public static bool IsExternal(string schema)
        => ExternalSchemas.Contains(schema);

    public static bool HasVerifiedEmail(IReadOnlyDictionary<string, string> claims)
        => claims.TryGetValue(EmailVerifiedClaim, out var value) && IsVerifiedEmailClaim(value);

    public static bool IsVerifiedEmailClaim(string? value)
        => bool.TryParse(value, out var isVerified) && isVerified;

    public static (string Schema, string DisplayName)[] ToSchemasWithDisplayNames(IEnumerable<string> schemas)
        => schemas.Select(schema => (schema, DisplayNames[schema])).ToArray();
}
