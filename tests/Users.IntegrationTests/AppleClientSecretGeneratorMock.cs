using AspNet.Security.OAuth.Apple;

namespace ActualChat.Users.IntegrationTests;

/// <summary>
/// Bypasses Apple private key requirements for testing. Embeds <c>options.ClientId</c>
/// into the returned secret so the token endpoint mock can verify that whatever
/// client id was used to sign the secret matches the one sent in the token request —
/// the real Apple endpoint enforces this, and we want our fakes to as well.
/// </summary>
public sealed class AppleClientSecretGeneratorMock : AppleClientSecretGenerator
{
    public const string SecretPrefix = "fake-client-secret:";

    public override Task<string> GenerateAsync(AppleGenerateClientSecretContext context)
        => Task.FromResult(SecretPrefix + context.Options.ClientId);
}
