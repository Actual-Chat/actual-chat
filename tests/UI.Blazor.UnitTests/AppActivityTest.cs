using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.UnitTests;

public class AppActivityTest: TestBase
{
    private ServiceProvider Services { get; }

    public AppActivityTest(ITestOutputHelper @out) : base(@out)
        => Services = new ServiceCollection()
            .AddTestLogging(Out)
            .AddSingleton(_ => new HostInfo {
                HostKind = HostKind.MauiApp,
                AppKind = AppKind.Ios,
                Environment = Environments.Development,
                IsTested = true,
            })
            .AddSingleton<UIHub>()
            .AddAlias<Hub, UIHub>()
            .AddSingleton<BackgroundStateTracker, MauiBackgroundStateTracker>()
            .AddFusion()
            .AddService<AppActivity, TestAppActivity>()
            .Services
            .BuildServiceProvider();

    [Fact]
    public async Task BasicTest()
    {
        var backgroundStateTracker = (MauiBackgroundStateTracker)Services.GetRequiredService<BackgroundStateTracker>();
        backgroundStateTracker.IsBackground.Value.Should().BeFalse();

        var appActivity = (TestAppActivity)Services.GetRequiredService<AppActivity>();
        appActivity.Start();
        appActivity.State.Value.Should().Be(ActivityState.Foreground);

        backgroundStateTracker.IsBackground.Value = true;
        await appActivity.State
            .When(x => x == ActivityState.BackgroundIdle)
            .WaitAsync(TimeSpan.FromSeconds(2));

        appActivity.SetIsActiveInBackground(true);
        await appActivity.State
            .When(x => x == ActivityState.BackgroundActive)
            .WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MassUpdateTest()
    {
        var random = Random.Shared;
        var log = Services.LogFor(GetType());
        var backgroundStateTracker = (MauiBackgroundStateTracker)Services.GetRequiredService<BackgroundStateTracker>();
        var appActivity = (TestAppActivity)Services.GetRequiredService<AppActivity>();
        appActivity.Start();

        using var cts = new CancellationTokenSource();
        // ReSharper disable AccessToDisposedClosure

        _ = BackgroundTask.Run(async () => {
            for (int i = 0; i < 10; i++) {
                await Task.Delay(random.Next(10,200), cts.Token);
                backgroundStateTracker.IsBackground.Value = random.Next(3) >= 1;
            }
        }, cts.Token);

        _ = BackgroundTask.Run(async () => {
            for (int i = 0; i < 10; i++) {
                await Task.Delay(random.Next(10,200), cts.Token);
                appActivity.SetIsActiveInBackground(random.Next(2) == 1);

            }
        }, cts.Token);

        _ = BackgroundTask.Run(async () => {
            await Task.Delay(5000, CancellationToken.None);
            cts.CancelAndDisposeSilently();
        }, CancellationToken.None);

        var stateChangeCount = 0;
        await foreach (var computed in appActivity.State.Computed.Changes(cts.Token).SuppressCancellation(cts.Token)) {
            log.LogInformation("Computed background state = {State}", computed.Value);
            stateChangeCount++;
        }

        // ReSharper restore AccessToDisposedClosure

        stateChangeCount.Should().BeGreaterThan(2);
    }
}

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Local
public class TestAppActivity(UIHub hub) : AppActivity(hub)
{
    private readonly MutableState<bool> _mustBeBackgroundActive
        = hub.StateFactory().NewMutable<bool>();

    [ComputeMethod]
    protected override async Task<bool> MustBeBackgroundActive(CancellationToken cancellationToken)
        => await _mustBeBackgroundActive.Use(cancellationToken).ConfigureAwait(false);

    public void SetIsActiveInBackground(bool value)
        => _mustBeBackgroundActive.Value = value;
}
