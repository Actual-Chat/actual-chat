using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;

namespace ActualChat.Testing.Passkeys;

/// <summary>
/// A P-256 WebAuthn authenticator in software: produces real attestation ("none" format) and
/// assertion responses for the options JSON the server hands out, so tests exercise Fido2NetLib's
/// verification rather than mocking it.
/// </summary>
public sealed class SoftwareAuthenticator(string rpId, string origin) : IDisposable
{
    public static readonly Guid Aaguid = new("0102030405060708090a0b0c0d0e0f10");

    private const byte FlagUserPresent = 0x01;
    private const byte FlagUserVerified = 0x04;
    private const byte FlagBackupEligible = 0x08;
    private const byte FlagBackedUp = 0x10;
    private const byte FlagAttestedCredentialData = 0x40;

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _credentialId = RandomNumberGenerator.GetBytes(32);

    private string RpId { get; } = rpId;
    public string CredentialId => Base64Url(_credentialId);
    public byte[]? UserHandle { get; private set; }
    public uint SignCount { get; set; }
    // When set, every response carries this counter verbatim instead of the incremented SignCount
    public uint? FixedSignCount { get; set; }
    public bool IsBackupEligible { get; init; } = true;
    public bool IsBackedUp { get; init; } = true;
    public string Origin { get; set; } = origin;

    public void Dispose()
        => _key.Dispose();

    public string CreateAttestationJson(string creationOptionsJson)
    {
        using var options = JsonDocument.Parse(creationOptionsJson);
        var challenge = options.RootElement.GetProperty("challenge").GetString()!;
        UserHandle = System.Buffers.Text.Base64Url.DecodeFromChars(
            options.RootElement.GetProperty("user").GetProperty("id").GetString()!);
        var clientDataJson = ClientDataJson("webauthn.create", challenge);
        var authData = AuthData(withCredential: true);
        var attestationObject = AttestationObject(authData);
        return JsonSerializer.Serialize(new {
            id = CredentialId,
            rawId = CredentialId,
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new {
                clientDataJSON = Base64Url(clientDataJson),
                attestationObject = Base64Url(attestationObject),
                transports = new[] { "internal" },
            },
        });
    }

    public string CreateAssertionJson(string assertionOptionsJson)
    {
        using var options = JsonDocument.Parse(assertionOptionsJson);
        var challenge = options.RootElement.GetProperty("challenge").GetString()!;
        var clientDataJson = ClientDataJson("webauthn.get", challenge);
        var authData = AuthData(withCredential: false);
        var signed = authData.Concat(SHA256.HashData(clientDataJson)).ToArray();
        var signature = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return JsonSerializer.Serialize(new {
            id = CredentialId,
            rawId = CredentialId,
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new {
                clientDataJSON = Base64Url(clientDataJson),
                authenticatorData = Base64Url(authData),
                signature = Base64Url(signature),
                userHandle = UserHandle is null ? null : Base64Url(UserHandle),
            },
        });
    }

    public static string Base64Url(byte[] bytes)
        => System.Buffers.Text.Base64Url.EncodeToString(bytes);

    // Private methods

    private byte[] ClientDataJson(string type, string challenge)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            type,
            challenge,
            origin = Origin,
            crossOrigin = false,
        }));

    private byte[] AuthData(bool withCredential)
    {
        var flags = (byte)(FlagUserPresent | FlagUserVerified);
        if (IsBackupEligible)
            flags |= FlagBackupEligible;
        if (IsBackedUp)
            flags |= FlagBackedUp;
        if (withCredential)
            flags |= FlagAttestedCredentialData;

        var counter = FixedSignCount ?? ++SignCount;
        var buffer = new List<byte>(SHA256.HashData(Encoding.UTF8.GetBytes(RpId)));
        buffer.Add(flags);
        buffer.AddRange([(byte)(counter >> 24), (byte)(counter >> 16), (byte)(counter >> 8), (byte)counter]);
        if (!withCredential)
            return buffer.ToArray();

        buffer.AddRange(Aaguid.ToByteArray(bigEndian: true));
        buffer.AddRange([(byte)(_credentialId.Length >> 8), (byte)_credentialId.Length]);
        buffer.AddRange(_credentialId);
        buffer.AddRange(CoseKey());
        return buffer.ToArray();
    }

    private byte[] CoseKey()
    {
        var q = _key.ExportParameters(false).Q;
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(5);
        writer.WriteInt32(1);
        writer.WriteInt32(2); // kty: EC2
        writer.WriteInt32(3);
        writer.WriteInt32(-7); // alg: ES256
        writer.WriteInt32(-1);
        writer.WriteInt32(1); // crv: P-256
        writer.WriteInt32(-2);
        writer.WriteByteString(Pad32(q.X!)); // x
        writer.WriteInt32(-3);
        writer.WriteByteString(Pad32(q.Y!)); // y
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] AttestationObject(byte[] authData)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteTextString("fmt");
        writer.WriteTextString("none");
        writer.WriteTextString("attStmt");
        writer.WriteStartMap(0);
        writer.WriteEndMap();
        writer.WriteTextString("authData");
        writer.WriteByteString(authData);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] Pad32(byte[] value)
        => value.Length >= 32 ? value : new byte[32 - value.Length].Concat(value).ToArray();
}
