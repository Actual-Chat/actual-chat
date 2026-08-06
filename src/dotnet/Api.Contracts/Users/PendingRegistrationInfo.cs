
namespace ActualChat.Users;

/// <summary>
/// JSON-serialized payload of <c>SessionTemporals[PendingRegistrationKey]</c>.
/// Provider/Identifier are display hints; Token is an opaque server-issued
/// SecureToken whose contents carry the actual sign-in payload.
/// </summary>
[DataContract]
public sealed record PendingRegistrationInfo(
    [property: DataMember(Order = 0)] string Provider,
    [property: DataMember(Order = 1)] string Identifier,
    [property: DataMember(Order = 2)] string Token)
{
    public string ToJson()
        => JsonSerializer.Serialize(this);

    public static PendingRegistrationInfo? TryParseJson(string? json)
    {
        if (json.IsNullOrEmpty())
            return null;
        try {
            return JsonSerializer.Deserialize<PendingRegistrationInfo>(json);
        }
        catch (JsonException) {
            return null;
        }
    }
}
