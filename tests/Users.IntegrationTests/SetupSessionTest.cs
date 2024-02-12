using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection)), Trait("Category", nameof(UserCollection))]
public class SetupSessionTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task SetupSessionBugTest1()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();

        await using var tester = appHost.NewWebClientTester(Out);
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
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();

        await using var tester = appHost.NewWebClientTester(Out);

        var auth = services.GetRequiredService<IAuth>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 10), async (_, cancellationToken) => {
            var session = Session.New();
            await commander.Call(new AuthBackend_SetupSession(session), cancellationToken);
            var sessionInfo = await auth.GetSessionInfo(session, cancellationToken);
            sessionInfo.Should().NotBeNull();
            sessionInfo.GetGuestId().IsGuest.Should().BeTrue();
        });
    }
}
