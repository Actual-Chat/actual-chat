using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.UnitTests;

public class BackgroundActivityUITest: TestBase
{
    private IServiceProvider Services { get; }
    private IServiceProvider ScopedServices { get; }

    public BackgroundActivityUITest(ITestOutputHelper @out) : base(@out)
    {
        var hostInfo = new HostInfo {
            HostKind = HostKind.MauiApp,
            AppKind = AppKind.Ios,
            Environment = Environments.Development,
            BaseUrl = $"https://{Constants.Hosts.LocalVoxt}",
            IsTested = true,
        };
        Services = new ServiceCollection()
            .AddTestLogging(Out)
            .AddSingleton(_ => hostInfo)
            .AddSingleton(c => new Features(c))
            .AddSingleton(_ => new UrlMapper(hostInfo))
            .AddScoped<UIHub>()
            .AddSingleton<BackgroundStateTracker, TestBackgroundStateTracker>()
            .AddFusion(fusion => {
                fusion.AddBlazor();
                fusion.AddService<BackgroundActivityUI, TestBackgroundActivityUI>();
            })
            .BuildServiceProvider();
        ScopedServices = Services.CreateScope().ServiceProvider;
    }

    [Fact]
    public async Task BasicTest()
    {
        var backgroundStateTracker = (TestBackgroundStateTracker)Services.GetRequiredService<BackgroundStateTracker>();
        backgroundStateTracker.SetBackgroundState(false);
        backgroundStateTracker.IsBackground.Value.Should().BeFalse();

        var backgroundActivity = (TestBackgroundActivityUI)ScopedServices.GetRequiredService<BackgroundActivityUI>();
        backgroundActivity.Start();
        backgroundActivity.State.Value.Should().Be(BackgroundActivityState.Foreground);

        backgroundStateTracker.SetBackgroundState(true);
        await backgroundActivity.State.Computed
            .When(x => x == BackgroundActivityState.BackgroundIdle)
            .WaitAsync(TimeSpan.FromSeconds(2));

        backgroundActivity.SetIsActiveInBackground(true);
        await backgroundActivity.State.Computed
            .When(x => x == BackgroundActivityState.BackgroundActive)
            .WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MassUpdateTest()
    {
        var random = Random.Shared;
        var log = Services.LogFor(GetType());
        var backgroundStateTracker = (TestBackgroundStateTracker)Services.GetRequiredService<BackgroundStateTracker>();
        backgroundStateTracker.SetBackgroundState(false);
        backgroundStateTracker.IsBackground.Value.Should().BeFalse();
        var backgroundActivity = (TestBackgroundActivityUI)ScopedServices.GetRequiredService<BackgroundActivityUI>();
        backgroundActivity.Start();

        using var cts = new CancellationTokenSource();
        // ReSharper disable AccessToDisposedClosure

        _ = BackgroundTask.Run(async () => {
            for (int i = 0; i < 10; i++) {
                await Task.Delay(random.Next(10,200), cts.Token);
                backgroundStateTracker.SetBackgroundState(random.Next(3) >= 1);
            }
        }, cts.Token);

        _ = BackgroundTask.Run(async () => {
            for (int i = 0; i < 10; i++) {
                await Task.Delay(random.Next(10,200), cts.Token);
                backgroundActivity.SetIsActiveInBackground(random.Next(2) == 1);
            }
        }, cts.Token);

        _ = BackgroundTask.Run(async () => {
            await Task.Delay(5000, CancellationToken.None);
            cts.CancelAndDisposeSilently();
        }, CancellationToken.None);

        var stateChangeCount = 0;
        await foreach (var computed in backgroundActivity.State.Computed.Changes(cts.Token).SuppressCancellation(cts.Token)) {
            log.LogInformation("Computed background state = {State}", computed.Value);
            stateChangeCount++;
        }

        // ReSharper restore AccessToDisposedClosure

        stateChangeCount.Should().BeGreaterThanOrEqualTo(2);
    }
}

public class TestBackgroundStateTracker : BackgroundStateTracker
{
    private readonly MutableState<bool> _isBackgroundState;

    public override IState<bool> IsBackground => _isBackgroundState;

    public TestBackgroundStateTracker(IServiceProvider services)
    {
        _isBackgroundState = services.StateFactory()
            .NewMutable(false, StateCategories.Get(GetType(), nameof(IsBackground)));
    }

    public void SetBackgroundState(bool isBackground)
        => _isBackgroundState.Value = isBackground;
}

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Local
public class TestBackgroundActivityUI(UIHub hub) : BackgroundActivityUI(hub)
{
    private readonly MutableState<bool> _mustBeBackgroundActive
        = hub.StateFactory.NewMutable<bool>();

    [ComputeMethod]
    protected override async Task<bool> MustBeBackgroundActive(CancellationToken cancellationToken)
        => await _mustBeBackgroundActive.Use(cancellationToken).ConfigureAwait(false);

    public void SetIsActiveInBackground(bool value)
        => _mustBeBackgroundActive.Value = value;
}
