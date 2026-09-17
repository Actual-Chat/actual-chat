using System.Text;
using ActualChat.Module;
using ActualChat.Testing.Host;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Core.Server.IntegrationTests.Web;

public class RpcCheckTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(RpcCheckTest)}", @out)
{
    private const int ProbeSize = 64 * 1024;
    private const string RussianIP = "77.88.55.242";
    private const string BritishIP = "81.2.69.142";

    [Fact]
    public async Task ShouldAnswerOkWithoutASize()
    {
        // arrange
        await using var host = await NewAppHost(WithProbeCountries("*"));
        using var httpClient = host.NewHttpClient();

        // act
        var payload = await httpClient.GetByteArrayAsync("rpc/check");

        // assert
        Encoding.UTF8.GetString(payload).Should().Be("ok",
            because: "the reachability probe must answer even when RPC itself can't get through");
    }

    [Fact]
    public async Task ShouldServeTheRequestedSizeToASession()
    {
        // arrange
        await using var host = await NewAppHost(WithProbeCountries("*"));
        using var httpClient = host.NewHttpClient();
        var session = Session.New();
        httpClient.DefaultRequestHeaders.Add(Constants.Session.HeaderName, session.Id);

        // act
        var payload = await httpClient.GetByteArrayAsync($"rpc/check?size={ProbeSize}");

        // assert
        payload.Length.Should().Be(ProbeSize);
    }

    [Fact]
    public async Task ShouldNotServeASizedPayloadAnonymously()
    {
        // arrange
        await using var host = await NewAppHost(WithProbeCountries("*"));
        using var httpClient = host.NewHttpClient();

        // act
        var payload = await httpClient.GetByteArrayAsync($"rpc/check?size={ProbeSize}");

        // assert
        Encoding.UTF8.GetString(payload).Should().Be("ok",
            because: "an anonymous caller must not be able to pull a large payload on demand");
    }

    [Fact]
    public async Task ShouldServeASizedPayloadOnlyToListedCountries()
    {
        // arrange
        await using var host = await NewAppHost(WithProbeCountries("RU"));
        using var httpClient = host.NewHttpClient();
        var session = Session.New();
        httpClient.DefaultRequestHeaders.Add(Constants.Session.HeaderName, session.Id);

        // act
        httpClient.DefaultRequestHeaders.Add("X-Forwarded-For", RussianIP);
        var listed = await httpClient.GetByteArrayAsync($"rpc/check?size={ProbeSize}");
        httpClient.DefaultRequestHeaders.Remove("X-Forwarded-For");
        httpClient.DefaultRequestHeaders.Add("X-Forwarded-For", BritishIP);
        var unlisted = await httpClient.GetByteArrayAsync($"rpc/check?size={ProbeSize}");

        // assert
        listed.Length.Should().Be(ProbeSize);
        Encoding.UTF8.GetString(unlisted).Should().Be("ok",
            because: "outside the listed countries the client is told there is nothing to measure");
    }

    [Fact]
    public async Task ShouldNotServeASizedPayloadWhenNoCountryIsListed()
    {
        // arrange
        await using var host = await NewAppHost();
        using var httpClient = host.NewHttpClient();
        var session = Session.New();
        httpClient.DefaultRequestHeaders.Add(Constants.Session.HeaderName, session.Id);
        httpClient.DefaultRequestHeaders.Add("X-Forwarded-For", RussianIP);

        // act
        var payload = await httpClient.GetByteArrayAsync($"rpc/check?size={ProbeSize}");

        // assert
        Encoding.UTF8.GetString(payload).Should().Be("ok",
            because: "the country list has no default, so an unconfigured server measures nobody");
    }

    [Fact]
    public async Task ShouldClampTheRequestedSize()
    {
        // arrange
        await using var host = await NewAppHost(WithProbeCountries("*"));
        using var httpClient = host.NewHttpClient();
        var session = Session.New();
        httpClient.DefaultRequestHeaders.Add(Constants.Session.HeaderName, session.Id);

        // act
        var tiny = await httpClient.GetByteArrayAsync("rpc/check?size=1");
        var huge = await httpClient.GetByteArrayAsync("rpc/check?size=99999999");

        // assert
        tiny.Length.Should().Be(1024);
        huge.Length.Should().Be(256 * 1024);
    }

    // Private methods

    private static Func<TestAppHostOptions, TestAppHostOptions> WithProbeCountries(string countries)
        => options => options with {
            ConfigureHost = (_, cfg) => cfg.AddInMemoryCollection(
                ($"{nameof(CoreSettings)}:{nameof(CoreServerSettings.RpcProbeCountries)}", countries)),
        };
}
