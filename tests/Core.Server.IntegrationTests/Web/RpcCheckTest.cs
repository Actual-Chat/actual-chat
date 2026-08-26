using System.Text;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Web;

public class RpcCheckTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(RpcCheckTest)}", @out)
{
    private const int ProbeSize = 64 * 1024;

    [Fact]
    public async Task ShouldAnswerOkWithoutASize()
    {
        // arrange
        await using var host = await NewAppHost();
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
        await using var host = await NewAppHost();
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
        await using var host = await NewAppHost();
        using var httpClient = host.NewHttpClient();

        // act
        var payload = await httpClient.GetByteArrayAsync($"rpc/check?size={ProbeSize}");

        // assert
        Encoding.UTF8.GetString(payload).Should().Be("ok",
            because: "an anonymous caller must not be able to pull a large payload on demand");
    }

    [Fact]
    public async Task ShouldClampTheRequestedSize()
    {
        // arrange
        await using var host = await NewAppHost();
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
}
