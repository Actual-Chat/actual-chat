using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ActivitiesUITest: TestBase
{
    private static readonly ChatId TestChatId = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private IServiceProvider Services { get; }
    private IServiceProvider ScopedServices { get; }

    public ActivitiesUITest(ITestOutputHelper @out) : base(@out)
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
            .AddScoped<AppUIHub>()
            .AddSingleton<BackgroundStateTracker, TestBackgroundStateTracker>()
            .AddFusion(fusion => {
                fusion.AddBlazor();
                fusion.AddService<ActivitiesUI, TestActivitiesUI>(ServiceLifetime.Scoped);
                fusion.AddService<TestActivitySource>(ServiceLifetime.Scoped);
            })
            .AddScoped<IActivitySource>(c => c.GetRequiredService<TestActivitySource>())
            .BuildServiceProvider();
        ScopedServices = Services.CreateScope().ServiceProvider;
    }

    [Fact]
    public async Task BasicTest()
    {
        var backgroundStateTracker = (TestBackgroundStateTracker)Services.GetRequiredService<BackgroundStateTracker>();
        backgroundStateTracker.SetBackgroundState(false);
        backgroundStateTracker.IsBackground.Value.Should().BeFalse();

        var activities = (TestActivitiesUI)ScopedServices.GetRequiredService<ActivitiesUI>();
        activities.Start();
        activities.State.Value.Should().Be(AppActivityState.Foreground);

        backgroundStateTracker.SetBackgroundState(true);
        await activities.State.Computed
            .When(x => x == AppActivityState.BackgroundIdle)
            .WaitAsync(TimeSpan.FromSeconds(2));

        activities.SetIsActiveInBackground(true);
        await activities.State.Computed
            .When(x => x == AppActivityState.BackgroundActive)
            .WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ActivitySignalsDriveState()
    {
        var backgroundStateTracker = (TestBackgroundStateTracker)Services.GetRequiredService<BackgroundStateTracker>();
        backgroundStateTracker.SetBackgroundState(true);

        var activities = (TestActivitiesUI)ScopedServices.GetRequiredService<ActivitiesUI>();
        var source = (TestActivitySource)ScopedServices.GetRequiredService<TestActivitySource>();
        activities.Start();
        await activities.State.Computed
            .When(x => x == AppActivityState.BackgroundIdle)
            .WaitAsync(TimeSpan.FromSeconds(2));

        // Any activity in the set triggers BackgroundActive - here a location share
        source.Set(new LocationActivity(TestChatId, 1));
        await activities.State.Computed
            .When(x => x == AppActivityState.BackgroundActive)
            .WaitAsync(TimeSpan.FromSeconds(2));

        source.Set(null);
        await activities.State.Computed
            .When(x => x == AppActivityState.BackgroundIdle)
            .WaitAsync(TimeSpan.FromSeconds(2));

        // The audio-intent hook alone still triggers BackgroundActive
        activities.SetAudioActivity(true);
        await activities.State.Computed
            .When(x => x == AppActivityState.BackgroundActive)
            .WaitAsync(TimeSpan.FromSeconds(2));

        activities.SetAudioActivity(false);
        await activities.State.Computed
            .When(x => x == AppActivityState.BackgroundIdle)
            .WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ActivitySetAggregatesSources()
    {
        var activities = (TestActivitiesUI)ScopedServices.GetRequiredService<ActivitiesUI>();
        var source = (TestActivitySource)ScopedServices.GetRequiredService<TestActivitySource>();

        (await activities.GetActivitySet(CancellationToken.None)).IsEmpty.Should().BeTrue();

        var location = new LocationActivity(TestChatId, 2);
        source.Set(location);
        var cSet = await Computed.Capture(() => activities.GetActivitySet(CancellationToken.None));
        cSet = await cSet.When(x => !x.IsEmpty).WaitAsync(TimeSpan.FromSeconds(2));
        cSet.Value.Primary.Should().Be(location);
    }

    [Fact]
    public async Task MassUpdateTest()
    {
        var log = Services.LogFor(GetType());
        var backgroundStateTracker = (TestBackgroundStateTracker)Services.GetRequiredService<BackgroundStateTracker>();
        backgroundStateTracker.SetBackgroundState(false);
        backgroundStateTracker.IsBackground.Value.Should().BeFalse();
        var activities = (TestActivitiesUI)ScopedServices.GetRequiredService<ActivitiesUI>();
        activities.Start();
        // Held true so every background flip below is a real Foreground <-> BackgroundActive
        // transition: with random inputs the observed change count depended on how the draws landed.
        activities.SetIsActiveInBackground(true);

        using var cts = new CancellationTokenSource();
        // ReSharper disable AccessToDisposedClosure

        _ = BackgroundTask.Run(async () => {
            for (var i = 0; i < 20; i++) {
                await Task.Delay(20, CancellationToken.None).ConfigureAwait(false);
                backgroundStateTracker.SetBackgroundState(i % 2 == 0);
            }
            await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            cts.CancelAndDisposeSilently();
        }, CancellationToken.None);

        var stateChangeCount = 0;
        await foreach (var computed in activities.State.Computed.Changes(cts.Token).SuppressCancellation(cts.Token)) {
            log.LogInformation("Computed activity state = {State}", computed.Value);
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
public class TestActivitiesUI(AppUIHub hub) : ActivitiesUI(hub)
{
    private readonly MutableState<bool> _mustBeBackgroundActive
        = hub.StateFactory.NewMutable<bool>();
    private readonly MutableState<bool> _hasAudioActivity
        = hub.StateFactory.NewMutable<bool>();

    [ComputeMethod]
    protected override async Task<bool> MustBeBackgroundActive(CancellationToken cancellationToken)
        => await _mustBeBackgroundActive.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    protected override async Task<bool> HasAudioActivity(CancellationToken cancellationToken)
        => await _hasAudioActivity.Use(cancellationToken).ConfigureAwait(false);

    public void SetIsActiveInBackground(bool value)
        => _mustBeBackgroundActive.Value = value;
    public void SetAudioActivity(bool value) => _hasAudioActivity.Value = value;
}

public class TestActivitySource(AppUIHub hub) : IActivitySource, IHasDisposeStatus
{
    private readonly MutableState<ActivityInfo?> _activity =
        hub.StateFactory.NewMutable<ActivityInfo?>();

    public bool IsDisposed => false;

    [ComputeMethod]
    public virtual async Task<ActivityInfo?> GetActivity(CancellationToken cancellationToken)
        => await _activity.Use(cancellationToken).ConfigureAwait(false);

    public void Set(ActivityInfo? activity) => _activity.Value = activity;
}
