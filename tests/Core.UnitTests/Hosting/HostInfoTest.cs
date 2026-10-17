using ActualChat.Hosting;

namespace ActualChat.Core.UnitTests.Hosting;

public class HostInfoTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("https://local.voxt.ai/", true)]
    [InlineData("https://wt1.local.voxt.ai/", true)]
    [InlineData("https://dev.voxt.ai/", true)]
    [InlineData("https://voxt.ai/", false)]
    [InlineData("http://localhost:7080/", false)]
    public void MustShowAllUIShouldFollowTheInstance(string baseUrl, bool expected)
    {
        // arrange
        var serverHostInfo = new HostInfo { BaseUrl = baseUrl, HostKind = HostKind.Server };
        var mauiHostInfo = new HostInfo { BaseUrl = baseUrl, HostKind = HostKind.MauiApp };

        // act + assert
        serverHostInfo.MustShowAllUI.Should().Be(expected);
        mauiHostInfo.MustShowAllUI.Should().Be(expected);
    }
}
