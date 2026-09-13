using System.Buffers.Text;
using System.Security.Cryptography;
using ActualChat.Testing.Passkeys;
using Fido2NetLib;
using Fido2NetLib.Objects;

namespace ActualChat.Users.IntegrationTests;

public class SoftwareAuthenticatorTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string RpId = "localhost";
    private const string Origin = "https://localhost";

    [Fact]
    public async Task AttestationAndAssertionShouldVerifyWithFido2()
    {
        // arrange
        var fido2 = new Fido2(new Fido2Configuration {
            ServerDomain = RpId,
            ServerName = "Test",
            Origins = new HashSet<string> { Origin },
        }, null);
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var user = new Fido2User { Id = RandomNumberGenerator.GetBytes(32), Name = "alice", DisplayName = "Alice" };
        var createOptions = fido2.RequestNewCredential(new RequestNewCredentialParams {
            User = user,
            AuthenticatorSelection = new AuthenticatorSelection {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });

        // act
        var attestationJson = authenticator.CreateAttestationJson(createOptions.ToJson());
        var attestation = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(attestationJson)!;
        var registered = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams {
            AttestationResponse = attestation,
            OriginalOptions = createOptions,
            IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true),
        }, CancellationToken.None);

        var assertionOptions = fido2.GetAssertionOptions(new GetAssertionOptionsParams {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required,
        });
        var assertionJson = authenticator.CreateAssertionJson(assertionOptions.ToJson());
        var assertion = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertionJson)!;
        var verified = await fido2.MakeAssertionAsync(new MakeAssertionParams {
            AssertionResponse = assertion,
            OriginalOptions = assertionOptions,
            StoredPublicKey = registered.PublicKey,
            StoredSignatureCounter = registered.SignCount,
            IsUserHandleOwnerOfCredentialIdCallback = (p, _) => Task.FromResult(p.UserHandle.SequenceEqual(user.Id)),
        }, CancellationToken.None);

        // assert
        registered.Id.Should().Equal(Base64Url.DecodeFromChars(authenticator.CredentialId));
        registered.IsBackupEligible.Should().BeTrue();
        registered.AaGuid.Should().Be(SoftwareAuthenticator.Aaguid);
        verified.SignCount.Should().Be(2, "the second ceremony bumps the counter");
        verified.IsBackedUp.Should().BeTrue();
    }
}
