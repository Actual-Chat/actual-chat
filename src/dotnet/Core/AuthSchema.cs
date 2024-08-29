namespace ActualChat;

public static class AuthSchema
{
    public const string Google = "Google";
    public const string Apple = "Apple";
    public const string Phone = "phone";
    public const string HashedPhone = "phone-hash";
    public const string Email = "email";
    public const string HashedEmail = "email-hash";

    public static readonly string[] AllExternal = [Google, Apple];

    public static readonly IReadOnlyDictionary<string, string> DisplayNames
        = new Dictionary<string, string>(StringComparer.Ordinal) {
            [Google] = "Google",
            [Apple] = "Apple",
            [Phone] = "Phone",
            [HashedPhone] = "Phone",
            [Email] = "Email",
            [HashedEmail] = "Email",
        };
    public static readonly IReadOnlySet<string> ExternalSchemas
        = new HashSet<string>(StringComparer.Ordinal) { Google, Apple };

    public static bool IsExternal(string schema)
#pragma warning disable MA0002
        => ExternalSchemas.Contains(schema);
#pragma warning restore MA0002

    public static (string Schema, string DisplayName)[] ToSchemasWithDisplayNames(IEnumerable<string> schemas)
        => schemas.Select(schema => (schema, DisplayNames[schema])).ToArray();
}
