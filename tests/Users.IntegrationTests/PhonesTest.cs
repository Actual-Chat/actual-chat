using ActualChat.Testing.Host;
using ActualChat.Users.Module;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class PhonesTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData("+1 (201) 555-0123", "1-2015550123")]  // Full international
    [InlineData("+44 7911 123456", "44-7911123456")]    // UK international
    [InlineData("+41 44 668 18 00", "41-446681800")]    // Swiss international
    public async Task ParseWithCountryFallback_FullInternationalNumber(string input, string expected)
    {
        var phones = AppHost.Services.GetRequiredService<IPhones>();
        var session = await CreateSessionWithIp("8.8.8.8"); // IP doesn't matter here

        var result = await phones.ParseWithCountryFallback(session, input, CancellationToken.None);
        result.Should().NotBeNull();
        result.Should().Be(ActualChat.Phone.Parse(expected));
    }

    [Fact]
    public async Task ParseWithCountryFallback_InternationalWithoutPlus()
    {
        var phones = AppHost.Services.GetRequiredService<IPhones>();
        // No IP → no GeoIP, so '+' prefix fallback kicks in
        var session = await CreateSessionWithIp("");

        var result = await phones.ParseWithCountryFallback(session, "12015550123", CancellationToken.None);
        result.Should().NotBeNull();
        result.Should().Be(ActualChat.Phone.Parse("1-2015550123"));

        result = await phones.ParseWithCountryFallback(session, "447911123456", CancellationToken.None);
        result.Should().NotBeNull();
        result.Should().Be(ActualChat.Phone.Parse("44-7911123456"));
    }

    [Theory]
    [InlineData("8.8.8.8", "(201) 555-0123", "1-2015550123")]     // US IP → US region
    [InlineData("8.8.8.8", "650-924-7331", "1-6509247331")]       // US IP → US region, dashed format
    [InlineData("81.2.69.160", "7911 123456", "44-7911123456")]    // UK IP → UK region
    public async Task ParseWithCountryFallback_NationalNumberWithGeoIp(
        string ip, string input, string expected)
    {
        var phones = AppHost.Services.GetRequiredService<IPhones>();
        var session = await CreateSessionWithIp(ip);

        var result = await phones.ParseWithCountryFallback(session, input, CancellationToken.None);
        result.Should().NotBeNull();
        result.Should().Be(ActualChat.Phone.Parse(expected));
    }

    [Fact]
    public async Task ParseWithCountryFallback_PredefinedPhoneIgnoresGeoIp()
    {
        // App-review scenario: a predefined test number entered from a Singapore IP
        // must resolve to the predefined phone, not to a +65 national number.
        var phones = AppHost.Services.GetRequiredService<IPhones>();
        var settings = AppHost.Services.GetRequiredService<UsersSettings>();
        var session = await CreateSessionWithIp("165.21.0.1"); // SG
        var oldPredefinedTotps = settings.PredefinedTotps;
        settings.PredefinedTotps = new Dictionary<string, int> {
            { "15551234567", 111111 },
        };
        try {
            var result = await phones.ParseWithCountryFallback(session, "15551234567", CancellationToken.None);
            result.Should().Be(ActualChat.Phone.Parse("1-5551234567"));
        }
        finally {
            settings.PredefinedTotps = oldPredefinedTotps;
        }
    }

    [Fact]
    public async Task ParseWithCountryFallback_PrefersValidPlusPrefixOverInvalidGeoIpParse()
    {
        var phones = AppHost.Services.GetRequiredService<IPhones>();
        var session = await CreateSessionWithIp("8.8.8.8"); // US

        // Not a valid US number, but a valid UK number with '+' prefix
        var result = await phones.ParseWithCountryFallback(session, "447911123456", CancellationToken.None);
        result.Should().Be(ActualChat.Phone.Parse("44-7911123456"));
    }

    [Fact]
    public async Task ParseWithCountryFallback_PrefersPossibleGeoIpParseOverPossiblePlusPrefix()
    {
        var phones = AppHost.Services.GetRequiredService<IPhones>();
        var session = await CreateSessionWithIp("8.8.8.8"); // US

        // Possible-but-invalid both as a US national number and as "+55..." (Brazil);
        // the GeoIP interpretation must win.
        var result = await phones.ParseWithCountryFallback(session, "5555555500", CancellationToken.None);
        result.Should().Be(ActualChat.Phone.Parse("1-5555555500"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("*111#")]
    public async Task ParseWithCountryFallback_UnparsableInput(string input)
    {
        var phones = AppHost.Services.GetRequiredService<IPhones>();
        var session = await CreateSessionWithIp("8.8.8.8");

        var result = await phones.ParseWithCountryFallback(session, input, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ParseWithCountryFallback_FallsBackToPlainParse()
    {
        // When no GeoIP data available, should still parse valid international numbers
        var phones = AppHost.Services.GetRequiredService<IPhones>();
        var session = await CreateSessionWithIp(""); // No IP → no GeoIP

        var result = await phones.ParseWithCountryFallback(session, "+1 (201) 555-0123", CancellationToken.None);
        result.Should().NotBeNull();
        result.Should().Be(ActualChat.Phone.Parse("1-2015550123"));
    }

    private async Task<Session> CreateSessionWithIp(string ipAddress)
    {
        var session = Session.New();
        await Commander.Call(new SessionsBackend_Upsert(session) { IPAddress = ipAddress });
        return session;
    }
}
