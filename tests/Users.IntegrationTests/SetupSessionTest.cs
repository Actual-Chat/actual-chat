using ActualChat.Testing.Host;
using Stl.Fusion.Authentication.Commands;

namespace ActualChat.Users.IntegrationTests;

public class SetupSessionTest : AppHostTestBase
{
    public SetupSessionTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task SetupSessionBugTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var commander = services.Commander();

        await using var tester = appHost.NewWebClientTester();
        var session = tester.Session;

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => commander.Call(new SetupSessionCommand(session)))
            .ToArray();
        await Task.WhenAll(tasks);

        var auth = services.GetRequiredService<IAuth>();
        var sessionInfo = auth.GetSessionInfo(session);
        sessionInfo.Should().NotBeNull();
    }
}
