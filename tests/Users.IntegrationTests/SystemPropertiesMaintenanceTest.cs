using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public sealed class SystemPropertiesMaintenanceTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task InvalidateEverythingShouldInvalidateOnTheApiHost()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueBobAdmin();
        var properties = AppHost.Services.GetRequiredService<ISystemProperties>();
        var computed = await Computed.Capture(() => properties.GetServerApiInfo("2.15", default));
        computed.IsConsistent().Should().BeTrue();

        // act
        await tester.Commander.Call(new SystemProperties_InvalidateEverything(tester.Session));

        // assert
        await TestWait.WhenPolled(() => computed.IsConsistent().Should().BeFalse());
    }

    [Fact]
    public async Task MaintenanceShouldRejectNonAdmins()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();

        // act
        var invalidate = () => tester.Commander.Call(new SystemProperties_InvalidateEverything(tester.Session));
        var prune = () => tester.Commander.Call(new SystemProperties_PruneComputedGraph(tester.Session));

        // assert
        await invalidate.Should().ThrowAsync<Exception>();
        await prune.Should().ThrowAsync<Exception>();
    }
}
