using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

public class SetupSessionTest : AppHostTestBase
{
    public SetupSessionTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task SetupSessionBugTest1()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var commander = services.Commander();

        await using var tester = appHost.NewWebClientTester();
        var session = tester.Session;

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => commander.Call(new AuthBackend_SetupSession(session)))
            .ToArray();
        await Task.WhenAll(tasks);

        var auth = services.GetRequiredService<IAuth>();
        var sessionInfo = await auth.GetSessionInfo(session);
        sessionInfo.Should().NotBeNull();
        sessionInfo.GetGuestId().IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task SetupSessionBugTest2()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var commander = services.Commander();

        await using var tester = appHost.NewWebClientTester();

        var sessionFactory = services.GetRequiredService<ISessionFactory>();
        var auth = services.GetRequiredService<IAuth>();

        await Parallel.ForEachAsync(Enumerable.Range(0, 10), async (_, cancellationToken) => {
            var session = sessionFactory.CreateSession();
            await commander.Call(new AuthBackend_SetupSession(session), cancellationToken);
            var sessionInfo = await auth.GetSessionInfo(session, cancellationToken);
            sessionInfo.Should().NotBeNull();
            sessionInfo.GetGuestId().IsGuest.Should().BeTrue();
        });
    }
}
