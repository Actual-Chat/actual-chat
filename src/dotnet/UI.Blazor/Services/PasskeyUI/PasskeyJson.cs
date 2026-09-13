using System.Buffers.Text;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// WebAuthn JSON for native clients that hand back raw blobs (Apple) instead of a serialized credential.
/// </summary>
public static class PasskeyJson
{
    public sealed record CreationOptions(string RpId, string UserName, byte[] UserId, byte[] Challenge);
    public sealed record RequestOptions(string RpId, byte[] Challenge);

    public static CreationOptions ParseCreationOptions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var user = root.GetProperty("user");
        return new CreationOptions(
            root.GetProperty("rp").GetProperty("id").GetString()!,
            user.GetProperty("name").GetString()!,
            Base64Url.DecodeFromChars(user.GetProperty("id").GetString()!),
            Base64Url.DecodeFromChars(root.GetProperty("challenge").GetString()!));
    }

    public static RequestOptions ParseRequestOptions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new RequestOptions(
            root.GetProperty("rpId").GetString()!,
            Base64Url.DecodeFromChars(root.GetProperty("challenge").GetString()!));
    }

    public static string Attestation(byte[] credentialId, byte[] clientDataJson, byte[] attestationObject)
    {
        var id = Base64Url.EncodeToString(credentialId);
        return JsonSerializer.Serialize(new {
            id,
            rawId = id,
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new {
                clientDataJSON = Base64Url.EncodeToString(clientDataJson),
                attestationObject = Base64Url.EncodeToString(attestationObject),
                transports = new[] { "internal" },
            },
        });
    }

    public static string Assertion(
        byte[] credentialId, byte[] clientDataJson, byte[] authenticatorData, byte[] signature, byte[]? userHandle)
    {
        var id = Base64Url.EncodeToString(credentialId);
        return JsonSerializer.Serialize(new {
            id,
            rawId = id,
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new {
                clientDataJSON = Base64Url.EncodeToString(clientDataJson),
                authenticatorData = Base64Url.EncodeToString(authenticatorData),
                signature = Base64Url.EncodeToString(signature),
                userHandle = userHandle is null ? null : Base64Url.EncodeToString(userHandle),
            },
        });
    }
}
