using ActualChat.Geo;

namespace ActualChat.Core.Server.UnitTests.Geo;

public class GeoIPTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("98.184.227.207", "US")]   // Cox Communications, California
    [InlineData("81.2.69.142", "GB")]      // London, UK
    [InlineData("77.88.55.242", "RU")]     // Yandex, Russia
    [InlineData("101.0.86.43", "AU")]      // Australia
    [InlineData("5.9.243.187", "DE")]      // Hetzner, Germany
    [InlineData("2607:f8b0:4004:800::200e", "US")] // Google IPv6
    [InlineData("2a00:1450:4009:826::2004", "GB")] // Google GB IPv6
    [InlineData("192.168.1.1", null)]      // Private IPv4
    [InlineData("127.0.0.1", null)]        // Loopback
    [InlineData("0.0.0.0", null)]          // None
    [InlineData("::1", null)]              // IPv6 loopback
    [InlineData("NotAnIP", null)]          // Not an IP -> null
    public async Task ToCountryCodeTest(string ipAddress, string? expected)
    {
        var country = await GeoIP.ToCountryCode(ipAddress);
        country.Should().Be(expected);
    }

    [Theory]
    [InlineData("98.184.227.207", "United States")]
    [InlineData("81.2.69.142", "United Kingdom")]
    [InlineData("77.88.55.242", "Russia")]
    [InlineData("192.168.1.1", null)]
    [InlineData("NotAnIP", null)]
    public async Task ToCountryNameTest(string ipAddress, string? expected)
    {
        var name = await GeoIP.ToCountryName(ipAddress);
        name.Should().Be(expected);
    }

    [Theory]
    [InlineData("81.2.69.142", "East Finchley, United Kingdom")]    // London, UK — city DB should resolve
    [InlineData("98.184.227.207", "Laguna Niguel, United States")] // Laguna Niguel, United States
    [InlineData("192.168.1.1")]
    [InlineData("77.88.55.242")] // Russia, but no City
    [InlineData("NotAnIP")]
    public async Task ToCityAndCountryNameTest(string ipAddress, string? expected = null)
    {
        var result = await GeoIP.ToCityAndCountryName(ipAddress);
        WriteLine($"{ipAddress} -> {result}");
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("98.184.227.207", 1)]      // US → +1
    [InlineData("81.2.69.142", 44)]        // GB → +44
    [InlineData("77.88.55.242", 7)]        // RU → +7
    [InlineData("101.0.86.43", 61)]        // AU → +61
    [InlineData("5.9.243.187", 49)]        // DE → +49
    [InlineData("2607:f8b0:4004:800::200e", 1)]  // Google IPv6 → US
    [InlineData("2a00:1450:4009:826::2004", 44)] // Google GB IPv6 → GB
    [InlineData("192.168.1.1", 0)]         // Private IPv4 → 0
    [InlineData("127.0.0.1", 0)]           // Loopback → 0
    [InlineData("::1", 0)]                 // IPv6 loopback → 0
    [InlineData("NotAnIP", 0)]             // Not an IP -> 0
    public async Task ToPhonePrefixTest(string ipAddress, int expected)
    {
        var prefix = await GeoIP.ToPhonePrefix(ipAddress);
        prefix.Should().Be(expected);
    }
}
