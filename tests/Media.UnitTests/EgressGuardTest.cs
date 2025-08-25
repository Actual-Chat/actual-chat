using ActualChat.Hosting;
using ActualChat.Media.Module;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Media.UnitTests;

public class EgressGuardTest
{
    private readonly EgressGuard _sut = new (new HostInfo {
            Environment = Environments.Production,
            IsTested = true,
        },
        new MediaSettings(),
        NullLogger<EgressGuard>.Instance);

    [Theory]
    [InlineData("voxt.ai")]
    [InlineData("cdn.voxt.ai")]
    [InlineData("media.voxt.ai")]
    [InlineData("actual.chat")]
    [InlineData("cdn.actual.chat")]
    [InlineData("media.actual.chat")]
    [InlineData("8.8.8.8")] // Public IP (Google DNS)
    [InlineData("104.16.124.96")] // Public IP (Cloudflare)
    [InlineData("2001:4860:4860::8888")] // Public IPv6 (Google DNS)
    public async Task ShouldAllow(string host)
    {
        var result = await _sut.IsAllowed(host);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("voxt.a1")]
    [InlineData("voxt.al")]
    [InlineData("local.voxt.ai")]
    [InlineData("local.actual.chat")]
    [InlineData("svc.cluster.local")]
    [InlineData("192.168.1.1")] // Private IP
    [InlineData("10.0.0.1")] // Private IP
    [InlineData("172.16.0.1")] // Private IP
    [InlineData("127.0.0.1")] // Localhost
    [InlineData("0.0.0.0")] // Special IP
    [InlineData("169.254.0.1")] // Link-local
    [InlineData("fc00::")] // Unique local IPv6
    [InlineData("::1")] // Localhost IPv6
    public async Task ShouldNotAllow(string host)
    {
        var result = await _sut.IsAllowed(host);
        result.Should().BeFalse();
    }
}
