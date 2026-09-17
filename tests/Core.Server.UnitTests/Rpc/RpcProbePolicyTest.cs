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
        var shouldMeasure = await policy.ShouldMeasure(ipAddress);

        // assert
        shouldMeasure.Should().Be(expected);
    }
}
