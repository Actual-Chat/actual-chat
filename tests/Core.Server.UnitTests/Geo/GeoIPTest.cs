using System.Net;
using ActualChat.Geo;

namespace ActualChat.Core.Server.UnitTests.Geo;

public class GeoIPTest
{
    [Fact]
    public async Task ToCountryCodeTest()
    {
        // US IP (Cox Communications, California)
        var usIp = IPAddress.Parse("98.184.227.207");
        var country = await GeoIP.ToCountryCode(usIp);
        country.Should().Be("US");
    }

    [Fact]
    public async Task ToPhonePrefixTest()
    {
        var usIp = IPAddress.Parse("98.184.227.207");
        var prefix = await GeoIP.ToPhonePrefix(usIp);
        prefix.Should().Be(1);
    }

    [Fact]
    public async Task ToCountryCode_PrivateIP_ReturnsNull()
    {
        var privateIp = IPAddress.Parse("192.168.1.1");
        var country = await GeoIP.ToCountryCode(privateIp);
        country.Should().BeNull();
    }

    [Fact]
    public async Task ToPhonePrefix_PrivateIP_ReturnsZero()
    {
        var privateIp = IPAddress.Parse("192.168.1.1");
        var prefix = await GeoIP.ToPhonePrefix(privateIp);
        prefix.Should().Be(0);
    }
}
