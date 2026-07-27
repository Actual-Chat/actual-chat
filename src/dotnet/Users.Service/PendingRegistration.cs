using ActualChat.Security;

namespace ActualChat.Users;

// Encrypted payload kept inside the SecureToken referenced by
// SessionTemporals[PendingRegistrationKey]. Never leaves the server.

[DataContract, MessagePackObject(AllowPrivate = true)]
internal sealed partial record PendingRegistration(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] UserIdentity AuthenticatedIdentity,
    [property: DataMember, Key(2)] ApiMap<UserIdentity, string> Identities,
    [property: DataMember, Key(3)] ApiMap<string, string> Claims);

internal static class PendingRegistrationExt
{
    public static async ValueTask<string> Encode(
        this PendingRegistration payload,
        ISecureTokensBackend secureTokens,
        CancellationToken cancellationToken = default)
    {
        var bytes = MessagePackSerializer.Serialize(payload, cancellationToken: cancellationToken);
        var encoded = Convert.ToBase64String(bytes);
        var token = await secureTokens
            .Create(SecureTokenKind.PendingRegistration, encoded, cancellationToken)
            .ConfigureAwait(false);
        return token.Token;
    }

    public static PendingRegistration? TryDecode(
        ISecureTokensBackend secureTokens,
        string token,
        Session expectedSession)
    {
        var decrypted = secureTokens.TryDecrypt(SecureTokenKind.PendingRegistration, token);
        if (decrypted is null)
            return null;

        PendingRegistration payload;
        try {
            var bytes = Convert.FromBase64String(decrypted.Value);
            payload = MessagePackSerializer.Deserialize<PendingRegistration>(bytes);
        }
        catch (Exception) {
            return null;
        }
        return payload.Session == expectedSession ? payload : null;
    }
}
