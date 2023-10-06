using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Stl.Time.Testing;
using Stl.Versioning.Providers;

namespace ActualChat.UI.Blazor.UnitTests;

public class BackgroundUITest: TestBase
{
    private ServiceProvider Services { get; }

    public BackgroundUITest(ITestOutputHelper @out) : base(@out)
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
            .AddSingleton<IBackgroundActivityProvider>(c => c.GetRequiredService<BackgroundActivityProviderStub>())
            .ConfigureLogging(Out)
            .AddFusion()
            .AddService<BackgroundUI>()
            .AddService<BackgroundActivityProviderStub>()
            .Services
            .BuildServiceProvider();

    [Fact]
    public async Task BackgroundUIStateSyncTest()
    {
        var testClock = new TestClock();
        var backgroundUI = Services.GetRequiredService<BackgroundUI>();
        backgroundUI.State.Value.Should().Be(BackgroundState.Foreground);

        var backgroundHandler = backgroundUI as IBackgroundStateHandler;
        var activityHandler = (BackgroundActivityProviderStub)Services.GetRequiredService<IBackgroundActivityProvider>();

        backgroundHandler.SetBackgroundState(true);
        activityHandler.SetIsActive(true);

        backgroundUI.State.Value.Should().Be(BackgroundState.Foreground);

        backgroundUI.Start();
        await testClock.Delay(2500);

        var state = await backgroundUI.State.Use(CancellationToken.None);
        state.Should().Be(BackgroundState.BackgroundActive);
    }

    [Fact]
    public async Task BackgroundUIMassUpdateTest()
    {
        var testClock = new TestClock();
        var random = Random.Shared;
        var log = Services.LogFor<BackgroundUITest>();
        var backgroundUI = Services.GetRequiredService<BackgroundUI>();
        backgroundUI.State.Value.Should().Be(BackgroundState.Foreground);

        var backgroundHandler = backgroundUI as IBackgroundStateHandler;
        var activityHandler = (BackgroundActivityProviderStub)Services.GetRequiredService<IBackgroundActivityProvider>();

        backgroundUI.Start();
        var cts = new CancellationTokenSource();

        _ = BackgroundTask.Run(async () => {
            for (int i = 0; i < 10; i++) {
                await testClock.Delay(random.Next(10,200), cancellationToken: cts.Token);
                backgroundHandler.SetBackgroundState(random.Next(3) >= 1);

            }
        }, cts.Token);

        _ = BackgroundTask.Run(async () => {
            for (int i = 0; i < 10; i++) {
                await testClock.Delay(random.Next(10,200), cancellationToken: cts.Token);
                activityHandler.SetIsActive(random.Next(2) == 1);

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

        stateChangeCount.Should().BeGreaterThan(2);
    }
}

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Local
internal class BackgroundActivityProviderStub(IServiceProvider services): IBackgroundActivityProvider
{
    private readonly IMutableState<bool> _state = services.StateFactory().NewMutable<bool>();

    [ComputeMethod]
    public virtual async Task<bool> GetIsActive(CancellationToken cancellationToken)
        => await _state.Use(cancellationToken).ConfigureAwait(false);

    public void SetIsActive(bool state)
    {
        if (_state.Value == state)
            return;

        _state.Value = state;
    }
}
