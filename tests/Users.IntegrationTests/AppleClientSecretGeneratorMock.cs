using AspNet.Security.OAuth.Apple;

namespace ActualChat.Users.IntegrationTests;

/// <summary>
/// Bypasses Apple private key requirements for testing.
/// </summary>
public sealed class AppleClientSecretGeneratorMock : AppleClientSecretGenerator
{
    public override Task<string> GenerateAsync(AppleGenerateClientSecretContext context)
        => Task.FromResult("fake-client-secret");
}
