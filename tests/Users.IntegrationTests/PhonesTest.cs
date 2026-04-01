using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

public class PhonesTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(PhonesTest)}", TestAppHostOptions.Default, @out)
{
    [Theory(Timeout = 30_000)]
    [InlineData("+1 (201) 555-0123", "1-2015550123")]  // Full international
    [InlineData("+44 7911 123456", "44-7911123456")]    // UK international
    [InlineData("+41 44 668 18 00", "41-446681800")]    // Swiss international
    public async Task ParseWithCountryFallback_FullInternationalNumber(string input, string expected)
    {
        await using var h = await NewAppHost();
        var phones = h.Services.GetRequiredService<IPhones>();
        var session = await CreateSessionWithIp(h, "8.8.8.8"); // IP doesn't matter here

        var result = await phones.ParseWithCountryFallback(session, input, CancellationToken.None);
        result.Should().NotBeNull();
        result.Should().Be(ActualChat.Phone.Parse(expected));
    }

    [Fact(Timeout = 30_000)]
    public async Task ParseWithCountryFallback_InternationalWithoutPlus()
    {
        await using var h = await NewAppHost();
        var phones = h.Services.GetRequiredService<IPhones>();
        // No IP → no GeoIP, so '+' prefix fallback kicks in
        var session = await CreateSessionWithIp(h, "");

        var result = await phones.ParseWithCountryFallback(session, "12015550123", CancellationToken.None);
        result.Should().NotBeNull();
        result.Should().Be(ActualChat.Phone.Parse("1-2015550123"));

        result = await phones.ParseWithCountryFallback(session, "447911123456", CancellationToken.None);
        result.Should().NotBeNull();
        result.Should().Be(ActualChat.Phone.Parse("44-7911123456"));
    }

    [Theory(Timeout = 30_000)]
    [InlineData("8.8.8.8", "(201) 555-0123", "1-2015550123")]     // US IP → US region
    [InlineData("8.8.8.8", "650-924-7331", "1-6509247331")]       // US IP → US region, dashed format
    [InlineData("81.2.69.160", "7911 123456", "44-7911123456")]    // UK IP → UK region
    public async Task ParseWithCountryFallback_NationalNumberWithGeoIp(
        string ip, string input, string expected)
    {
        await using var h = await NewAppHost();
        var phones = h.Services.GetRequiredService<IPhones>();
        var session = await CreateSessionWithIp(h, ip);

        var result = await phones.ParseWithCountryFallback(session, input, CancellationToken.None);
        result.Should().NotBeNull();
        result.Should().Be(ActualChat.Phone.Parse(expected));
    }

    [Theory(Timeout = 30_000)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("*111#")]
    public async Task ParseWithCountryFallback_UnparsableInput(string input)
    {
        await using var h = await NewAppHost();
        var phones = h.Services.GetRequiredService<IPhones>();
        var session = await CreateSessionWithIp(h, "8.8.8.8");

        var result = await phones.ParseWithCountryFallback(session, input, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task ParseWithCountryFallback_FallsBackToPlainParse()
    {
        // When no GeoIP data available, should still parse valid international numbers
        await using var h = await NewAppHost();
        var phones = h.Services.GetRequiredService<IPhones>();
        var session = await CreateSessionWithIp(h, ""); // No IP → no GeoIP

        var result = await phones.ParseWithCountryFallback(session, "+1 (201) 555-0123", CancellationToken.None);
        result.Should().NotBeNull();
        result.Should().Be(ActualChat.Phone.Parse("1-2015550123"));
    }

    private static async Task<Session> CreateSessionWithIp(TestAppHost host, string ipAddress)
    {
        var session = Session.New();
        var commander = host.Services.Commander();
        await commander.Call(new SessionsBackend_Upsert(session) { IPAddress = ipAddress });
        return session;
    }
}
