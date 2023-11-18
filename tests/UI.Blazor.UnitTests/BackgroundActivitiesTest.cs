using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Stl.Time.Testing;
using Stl.Versioning.Providers;

namespace ActualChat.UI.Blazor.UnitTests;

public class BackgroundActivitiesTest: TestBase
{
    private ServiceProvider Services { get; }

    public BackgroundActivitiesTest(ITestOutputHelper @out) : base(@out)
        => Services = new ServiceCollection()
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton<IStateFactory>(c => new StateFactory(c))
            .AddSingleton(_ => LTagVersionGenerator.Default)
            .AddSingleton(_ => new HostInfo {
                AppKind = AppKind.MauiApp,
                ClientKind = ClientKind.Ios,
                Environment = HostInfo.DevelopmentEnvironment,
                IsTested = true,
            })
            .AddSingleton<IBackgroundActivities>(c => c.GetRequiredService<BackgroundActivitiesStub>())
            .ConfigureLogging(Out)
            .AddFusion()
            .AddService<BackgroundUI>()
            .AddService<BackgroundActivitiesStub>()
            .Services
            .BuildServiceProvider();

    [Fact]
    public async Task BackgroundUIStateSyncTest()
    {
        using var testClock = new TestClock();
        var backgroundUI = Services.GetRequiredService<BackgroundUI>();
        backgroundUI.State.Value.Should().Be(BackgroundState.Foreground);

        var backgroundHandler = backgroundUI as IBackgroundStateHandler;
        var activityHandler = (BackgroundActivitiesStub)Services.GetRequiredService<IBackgroundActivities>();

        backgroundHandler.SetIsBackground(true);
        activityHandler.SetIsActiveInBackground(true);

        backgroundUI.State.Value.Should().Be(BackgroundState.Foreground);

        backgroundUI.Start();
        await testClock.Delay(2500);

        var state = await backgroundUI.State.Use(CancellationToken.None);
        state.Should().Be(BackgroundState.BackgroundActive);
    }

    [Fact]
    public async Task BackgroundUIMassUpdateTest()
    {
        using var testClock = new TestClock();
        var random = Random.Shared;
        var log = Services.LogFor<BackgroundActivitiesTest>();
        var backgroundUI = Services.GetRequiredService<BackgroundUI>();
        backgroundUI.State.Value.Should().Be(BackgroundState.Foreground);

        var backgroundHandler = backgroundUI as IBackgroundStateHandler;
        var activityHandler = (BackgroundActivitiesStub)Services.GetRequiredService<IBackgroundActivities>();

        backgroundUI.Start();
        using var cts = new CancellationTokenSource();
        // ReSharper disable AccessToDisposedClosure

        _ = BackgroundTask.Run(async () => {
            for (int i = 0; i < 10; i++) {
                await testClock.Delay(random.Next(10,200), cancellationToken: cts.Token);
                backgroundHandler.SetIsBackground(random.Next(3) >= 1);

            }
        }, cts.Token);

        _ = BackgroundTask.Run(async () => {
            for (int i = 0; i < 10; i++) {
                await testClock.Delay(random.Next(10,200), cancellationToken: cts.Token);
                activityHandler.SetIsActiveInBackground(random.Next(2) == 1);

            }
        }, cts.Token);

        _ = BackgroundTask.Run(async () => {
            await testClock.Delay(5000, CancellationToken.None);
            cts.CancelAndDisposeSilently();
        }, CancellationToken.None);

        var stateChangeCount = 0;
        await foreach (var computed in backgroundUI.State.Computed.Changes(cts.Token).TrimOnCancellation(cts.Token)) {
            log.LogInformation("Computed background state = {State}", computed.Value);
            stateChangeCount++;
        }

        // ReSharper restore AccessToDisposedClosure

        stateChangeCount.Should().BeGreaterThan(2);
    }
}

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Local
#pragma warning disable CA1852
internal class BackgroundActivitiesStub(IServiceProvider services) : IBackgroundActivities
#pragma warning restore CA1852
{
    private readonly IMutableState<bool> _isActiveInBackground = services.StateFactory().NewMutable<bool>();

    [ComputeMethod]
    public virtual async Task<bool> IsActiveInBackground(CancellationToken cancellationToken)
        => await _isActiveInBackground.Use(cancellationToken).ConfigureAwait(false);

    public void SetIsActiveInBackground(bool value)
        => _isActiveInBackground.Value = value;
}
