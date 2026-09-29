using ActualChat.Module;
using ActualChat.Rpc;

namespace ActualChat.Core.Server.UnitTests.Rpc;

public class RpcProbePolicyTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string RussianIP = "77.88.55.242";
    private const string BritishIP = "81.2.69.142";

    [Theory]
    [InlineData("", RussianIP, false)]
    [InlineData("RU", RussianIP, true)]
    [InlineData("ru; ir", RussianIP, true)]
    [InlineData("RU,IR", BritishIP, false)]
    [InlineData("RU", "127.0.0.1", false)]
    [InlineData("RU", null, false)]
    [InlineData("*", BritishIP, true)]
    [InlineData("*", null, true)]
    public async Task ShouldMeasureOnlyListedCountries(string countries, string? ipAddress, bool expected)
    {
        // arrange
        var policy = new RpcProbePolicy(new CoreServerSettings { RpcProbeCountries = countries });

        // act
        var shouldMeasure = await policy.ShouldMeasure(ipAddress, "voxt.ai");

        // assert
        shouldMeasure.Should().Be(expected);
    }

    [Theory]
    [InlineData("RU", BritishIP, "kz1.edge.voxt.ai", true)]
    [InlineData("", null, "kz1.edge.dev.voxt.ai", true)]
    [InlineData("RU", BritishIP, "voxt.ai", false)]
    [InlineData("RU", BritishIP, "edge.voxt.ai", false)]
    [InlineData("RU", BritishIP, null, false)]
    public async Task ShouldMeasureAnyClientArrivingViaAnEdgeHost(
        string countries, string? ipAddress, string? host, bool expected)
    {
        // arrange
        var policy = new RpcProbePolicy(new CoreServerSettings { RpcProbeCountries = countries });

        // act
        var shouldMeasure = await policy.ShouldMeasure(ipAddress, host);

        // assert
        shouldMeasure.Should().Be(expected,
            because: "a relay forwards TLS as-is, so the origin sees the relay's address rather than the client's");
    }
}
