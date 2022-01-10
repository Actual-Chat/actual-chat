using ActualChat.Testing.Host;
using Stl.Fusion.Authentication.Commands;

namespace ActualChat.Users.IntegrationTests;

public class SetupSessionTest : AppHostTestBase
{
    public SetupSessionTest(ITestOutputHelper @out) : base(@out) { }

    [Fact(Skip = "Failing for now, to be fixed.")]
    public async Task SetupSessionBugTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
        using var tester = appHost.NewWebClientTester();
        var session = tester.Session;

        var services = appHost.Services;
        var commander = services.Commander();
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => commander.Call(new SetupSessionCommand(session)))
            .ToArray();
        await Task.WhenAll(tasks);
    }
}
