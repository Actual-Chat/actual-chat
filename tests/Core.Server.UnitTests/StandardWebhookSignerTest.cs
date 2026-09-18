using System.Security.Cryptography;
using System.Text;
using ActualChat.Security;

namespace ActualChat.Core.Server.UnitTests;

public class StandardWebhookSignerTest
{
    [Fact]
    public void SignShouldMatchIndependentHmac()
    {
        // arrange
        var secret = StandardWebhookSigner.NewSecret();
        var key = Convert.FromBase64String(
            secret[StandardWebhookSigner.SecretPrefix.Length..]);
        var hmacData = Encoding.UTF8.GetBytes("msg_1.1758104122.{}");
        var expected = Convert.ToBase64String(HMACSHA256.HashData(key, hmacData));

        // act
        var signature = StandardWebhookSigner.Sign(secret, "msg_1", 1758104122, "{}");

        // assert
        signature.Should().Be("v1," + expected);
    }

    [Fact]
    public void HeaderShouldCarryOneSignaturePerSecret()
    {
        // arrange
        var a = StandardWebhookSigner.NewSecret();
        var b = StandardWebhookSigner.NewSecret();

        // act
        var header = StandardWebhookSigner.SignatureHeader("id", 1, "{}", a, b);

        // assert
        header.Split(' ')
            .Should().HaveCount(2)
            .And.AllSatisfy(x => x.Should().StartWith("v1,"));
    }

    [Fact]
    public void VerifyShouldRejectStaleTimestamp()
    {
        // arrange
        var secret = StandardWebhookSigner.NewSecret();
        var header = StandardWebhookSigner.SignatureHeader("id", 1000, "{}", secret);
        var now = Moment.EpochStart + TimeSpan.FromSeconds(1000 + 600);

        // act & assert
        StandardWebhookSigner.Verify(
            secret, "id", 1000, "{}", header, TimeSpan.FromMinutes(5), now)
            .Should().BeFalse();
        StandardWebhookSigner.Verify(
            secret, "id", 1000, "{}", header, TimeSpan.FromMinutes(15), now)
            .Should().BeTrue();
    }

    [Fact]
    public void NewSecretShouldBe32RandomBytes()
        => Convert.FromBase64String(
                StandardWebhookSigner.NewSecret()[StandardWebhookSigner.SecretPrefix.Length..])
            .Should().HaveCount(32);
}
