using ActualChat.AspNetCore;
using ActualChat.Module;
using ActualChat.Rpc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Core.Server.UnitTests.Rpc;

public class RpcRelayAttestationVerifierTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string Key = "kz1 key";

    [Fact]
    public void ShouldParseKeysWithRotationAndPadding()
    {
        // act
        var keys = RpcRelayAttestationVerifier.ParseKeys("kz1=abc=; KZ1=def ;tr1=ghi==;bad;=nothing;empty=");

        // assert
        keys.Keys.Should().BeEquivalentTo("kz1", "tr1");
        keys["kz1"].Should().Equal("abc=", "def");
        keys["tr1"].Should().Equal("ghi==");
    }

    [Fact]
    public void ShouldReturnTheClientIPOfAValidAttestation()
    {
        // arrange
        var verifier = CreateVerifier();
        var attestation = RpcRelayAttestation.Sign("kz1", "81.2.69.142", DateTimeOffset.UtcNow, Key);

        // act & assert
        verifier.TryGetClientIP(attestation)!.ToString().Should().Be("81.2.69.142");
    }

    [Fact]
    public void ShouldTrustNoRelayWithoutKeys()
    {
        // arrange
        var verifier = new RpcRelayAttestationVerifier(new CoreServerSettings(), MomentClockSet.Default);
        var attestation = RpcRelayAttestation.Sign("kz1", "81.2.69.142", DateTimeOffset.UtcNow, Key);

        // act & assert
        verifier.TryGetClientIP(attestation).Should().BeNull();
    }

    [Fact]
    public void HttpContextShouldReportTheAttestedIPOverForwardedHeaders()
    {
        // arrange
        var attestation = RpcRelayAttestation.Sign("kz1", "81.2.69.142", DateTimeOffset.UtcNow, Key);
        var httpContext = CreateHttpContext(ToQuery(attestation));
        httpContext.Request.Headers["X-Forwarded-For"] = "10.0.0.1";

        // act & assert
        httpContext.GetRemoteIPAddress()!.ToString().Should().Be("81.2.69.142");
    }

    [Fact]
    public void HttpContextShouldIgnoreAForgedAttestation()
    {
        // arrange
        var attestation = RpcRelayAttestation.Sign("kz1", "81.2.69.142", DateTimeOffset.UtcNow, "stolen guess");
        var httpContext = CreateHttpContext(ToQuery(attestation));
        httpContext.Request.Headers["X-Forwarded-For"] = "10.0.0.1";

        // act & assert
        httpContext.GetRemoteIPAddress()!.ToString().Should().Be("10.0.0.1");
    }

    [Fact]
    public void HttpContextWithoutAttestationShouldKeepUsingForwardedHeaders()
    {
        // arrange
        var httpContext = CreateHttpContext("");
        httpContext.Request.Headers["X-Forwarded-For"] = "10.0.0.1";

        // act & assert
        httpContext.GetRemoteIPAddress()!.ToString().Should().Be("10.0.0.1");
    }

    // Private methods

    private static RpcRelayAttestationVerifier CreateVerifier()
        => new(new CoreServerSettings { RpcRelayKeys = $"kz1={Key}" }, MomentClockSet.Default);

    private static string ToQuery(string attestation)
        => $"?{RpcRelayAttestation.QueryParameterName}={Uri.EscapeDataString(attestation)}";

    private static DefaultHttpContext CreateHttpContext(string query)
    {
        var services = new ServiceCollection()
            .AddSingleton(CreateVerifier())
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.QueryString = new QueryString(query);
        return httpContext;
    }
}
