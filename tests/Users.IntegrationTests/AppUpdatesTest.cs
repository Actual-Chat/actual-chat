using ActualChat.Testing.Host;
using ActualChat.Users.Module;
using ActualLab.Fusion.Testing;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(AppUpdatesCollection))]
public sealed class AppUpdatesTest(AppUpdatesAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppUpdatesAppHostFixture>(fixture, @out)
{
    private static readonly Version OwnVersion = ApiConstants.BuildVersion;
    // Generous, because the collections of this suite run in parallel
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);
    private AppUpdates Service => field ??= (AppUpdates)AppHost.Services.GetRequiredService<IAppUpdates>();
    private ScriptedAppStoreProbes Probes
        => (ScriptedAppStoreProbes)AppHost.Services.GetRequiredService<AppStoreProbes>();
    private AppUpdateSettings Settings
        => AppHost.Services.GetRequiredService<UsersSettings>().AppUpdates;

    [Fact]
    public async Task ShouldReportWhatTheStoreServes()
    {
        // arrange
        const AppKind appKind = AppKind.Android;
        using var __ = await NewTestSettings(appKind);
        const string olderTrain = "1.0.0";
        var probe = Probes.Script(appKind, new(olderTrain, null));

        // act
        var behindTrain = await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            info!.VersionString.Should().Be(olderTrain);
            return info;
        }, TestTimeout);
        probe.Result = new(OwnVersion.ToString(), null);
        var published = await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            info!.VersionString.Should().Be(OwnVersion.ToString());
            return info;
        }, TestTimeout);
        var callCountWhenSettled = probe.CallCount;
        await Task.Delay(TimeSpan.FromSeconds(2));

        // assert
        behindTrain.Should().NotBeNull("the store build is installable whether or not the server has it");
        published.AppKind.Should().Be(appKind);
        probe.CallCount.Should().Be(callCountWhenSettled,
            "a store that serves this server's own build has nothing left to publish");
    }

    [Fact]
    public async Task ShouldDetectALaterBuildOnTheSameTrain()
    {
        // arrange - a hotfix published on the train the store already serves, both behind S
        const AppKind appKind = AppKind.Windows;
        using var __ = await NewTestSettings(appKind);
        var firstBuild = NewBuildBehindOwn(2);
        var hotfixBuild = NewBuildBehindOwn(1);
        var probe = Probes.Script(appKind, new(firstBuild, null));

        // act
        await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            info!.VersionString.Should().Be(firstBuild);
        }, TestTimeout);
        probe.Result = new(hotfixBuild, null);
        var hotfix = await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            info!.VersionString.Should().Be(hotfixBuild);
            return info;
        }, TestTimeout);

        // assert
        hotfix.Should().NotBeNull("the stores publish more than one build per train");
    }

    [Fact]
    public async Task ShouldCheckTheStoreOnlyOncePerRecheckPeriod()
    {
        // arrange - NextCheckAt is shared, so a second check finds the first one's result cached
        const AppKind appKind = AppKind.Windows;
        using var __ = await NewTestSettings(appKind);
        Settings.RecheckPeriods = [TimeSpan.FromMinutes(5)];
        var probe = Probes.Script(appKind, new(NewBuildBehindOwn(1), null));

        // act
        await WhenPolled(async () => {
            Service.Invalidate(appKind);
            var info = await Service.GetLatestUpdateInfo(appKind, default);
            info.Should().NotBeNull();
        });
        for (var i = 0; i < 5; i++) {
            Service.Invalidate(appKind);
            _ = await Service.GetLatestUpdateInfo(appKind, default);
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        // assert
        probe.CallCount.Should().Be(1, "the recheck period hasn't passed since the first check");
    }

    [Fact]
    public async Task ShouldNotProbeOtherStoresUntilPlayHasTheBuild()
    {
        // arrange
        const AppKind appKind = AppKind.Windows;
        using var __ = await NewTestSettings(appKind, isPlayGateEnabled: true);
        var playProbe = Probes.Script(AppKind.Android, new(NewBuildBehindOwn(1), null));
        var probe = Probes.Script(appKind, new(OwnVersion.ToString(), null));

        // act
        var whilePlayIsBehind = await Service.GetLatestUpdateInfo(appKind, default);
        await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(AppKind.Android, ct);
            info.Should().NotBeNull("Play has to be probed regardless");
        }, TestTimeout);
        await Task.Delay(TimeSpan.FromSeconds(2));
        _ = await Service.GetLatestUpdateInfo(appKind, default);
        var callCountWhilePlayIsBehind = probe.CallCount;
        playProbe.Result = new(OwnVersion.ToString(), null);
        var published = await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            info!.VersionString.Should().Be(OwnVersion.ToString());
            return info;
        }, TestTimeout);

        // assert
        whilePlayIsBehind.Should().BeNull();
        callCountWhilePlayIsBehind.Should().Be(0, "Play publishes first, so nothing else is worth asking");
        published.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldReportTheStoreBuildTheServerHasAlreadyMovedPast()
    {
        // arrange - the stores publish one build per train, and the server keeps deploying on top
        const AppKind appKind = AppKind.Android;
        using var __ = await NewTestSettings(appKind);
        var storeBuild = NewBuildBehindOwn(1);
        Probes.Script(appKind, new(storeBuild, null));

        // act
        var info = await ComputedTest.When(async ct => {
            var current = await Service.GetLatestUpdateInfo(appKind, ct);
            current.Should().NotBeNull();
            return current!;
        }, TestTimeout);

        // assert
        info.VersionString.Should().Be(storeBuild,
            "requiring the store to reach the server's own build skips the release");
    }

    [Fact]
    public async Task ShouldReportTheAnnouncedReleaseWhileTheServerIsAhead()
    {
        // arrange
        const AppKind appKind = AppKind.Windows;
        using var __ = await NewTestSettings(appKind);
        var announcedAt = Clocks.SystemClock.Now - TimeSpan.FromDays(1);
        var announced = new AppUpdateInfo(appKind, "1.0.0", "1.0.0.0", announcedAt, announcedAt);
        await Service.SetCachedStoreUpdateInfo(appKind, new AppUpdates.CachedUpdateInfo(announced), default);
        Probes.Script(appKind, new("1.0.0", null));

        // act
        var info = (AppUpdateInfo?)null;
        await WhenPolled(async () => {
            Service.Invalidate(appKind);
            info = await Service.GetLatestUpdateInfo(appKind, default);
            info.Should().NotBeNull();
        });

        // assert
        info!.VersionString.Should().Be("1.0.0", "a client older than the announced release still needs a banner");
    }

    [Fact]
    public async Task ShouldAnnounceADetectedReleaseOnlyAfterTheDelay()
    {
        // arrange
        const AppKind appKind = AppKind.Android;
        using var __ = await NewTestSettings(appKind);
        Settings.AnnounceDelay = TimeSpan.FromSeconds(6);
        var announcedAt = Clocks.SystemClock.Now - TimeSpan.FromDays(1);
        var announced = new AppUpdateInfo(appKind, "1.0.0", "1.0.0.0", announcedAt, announcedAt);
        await Service.SetCachedStoreUpdateInfo(appKind, new AppUpdates.CachedUpdateInfo(announced), default);
        Service.Invalidate(appKind);
        Probes.Script(appKind, new(OwnVersion.ToString(), null));

        // act
        // Polled, not computed-driven: while the detection is pending the value doesn't change,
        // so there is no invalidation to wait for
        var whilePending = (AppUpdateInfo?)null;
        await WhenPolled(async () => {
            var storeInfo = await Service.GetCachedStoreUpdateInfo(appKind, default);
            storeInfo!.PendingInfo!.VersionString.Should()
                .Be(OwnVersion.ToString(), "the release must be detected first");
            whilePending = await Service.GetLatestUpdateInfo(appKind, default);
        });
        var afterDelay = await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            info!.VersionString.Should().Be(OwnVersion.ToString());
            return info;
        }, TestTimeout);

        // assert
        whilePending!.VersionString.Should().Be("1.0.0", "a detected release is held back for AnnounceDelay");
        afterDelay!.DetectedAt.Should().BeGreaterThan(announcedAt);
    }

    [Fact]
    public async Task ShouldResumeProbingWhenThePendingWindowEndsWithTheServerAhead()
    {
        // arrange - this is what a server bump during the pending window leaves behind
        const AppKind appKind = AppKind.Ios;
        using var __ = await NewTestSettings(appKind);
        Settings.AnnounceDelay = TimeSpan.FromSeconds(3);
        var now = Clocks.SystemClock.Now;
        var pending = new AppUpdateInfo(appKind, "0.9.0", "0.9.0", now, now);
        var announced = new AppUpdateInfo(appKind, "0.8.0", "0.8.0", now, now);
        await Service.SetCachedStoreUpdateInfo(appKind, new AppUpdates.CachedUpdateInfo(announced, pending), default);
        var probe = Probes.Script(appKind, new("0.9.0", null));

        // act
        var whilePending = (AppUpdateInfo?)null;
        await WhenPolled(async () => {
            Service.Invalidate(appKind);
            whilePending = await Service.GetLatestUpdateInfo(appKind, default);
            whilePending.Should().NotBeNull();
        });
        var afterDelay = (AppUpdateInfo?)null;
        await WhenPolled(async () => {
            afterDelay = await Service.GetLatestUpdateInfo(appKind, default);
            afterDelay!.VersionString.Should().Be("0.9.0", "the pending release is announced once the window is out");
        });

        // assert
        whilePending!.VersionString.Should().Be("0.8.0");
        probe.CallCount.Should().BeGreaterThan(0, "checking resumes rather than waiting for a client");
    }

    [Fact]
    public async Task WasmShouldWaitOutTheRollingDeployGrace()
    {
        // arrange
        using var __ = await NewTestSettings(AppKind.Wasm);
        Settings.WasmGracePeriod = TimeSpan.FromHours(1);

        // act
        var withinGrace = (AppUpdateInfo?)null;
        await WhenPolled(async () => {
            Service.Invalidate(AppKind.Wasm);
            withinGrace = await Service.GetLatestUpdateInfo(AppKind.Wasm, default);
            withinGrace.Should().BeNull();
        });
        Settings.WasmGracePeriod = TimeSpan.Zero;
        var afterGrace = (AppUpdateInfo?)null;
        await WhenPolled(async () => {
            Service.Invalidate(AppKind.Wasm);
            afterGrace = await Service.GetLatestUpdateInfo(AppKind.Wasm, default);
            afterGrace.Should().NotBeNull();
        });

        // assert
        afterGrace!.VersionString.Should().Be(OwnVersion.ToString());
        afterGrace.StoreVersionString.Should().Be(ApiConstants.FullVersionString);
    }

    [Fact]
    public async Task ShouldReportNothingOutsideProductionUnlessOverridden()
    {
        // arrange
        const AppKind appKind = AppKind.MacOS;
        using var __ = await NewTestSettings(appKind);
        Settings.IsEnabled = null; // i.e. production instances only, and a test host isn't one

        // act
        var disabled = (AppUpdateInfo?)null;
        await WhenPolled(async () => {
            Service.Invalidate(appKind);
            disabled = await Service.GetLatestUpdateInfo(appKind, default);
            disabled.Should().BeNull();
        });
        Settings.Overrides = new Dictionary<string, string> { { appKind.ToString(), "9.9.9" } };
        var overridden = (AppUpdateInfo?)null;
        await WhenPolled(async () => {
            Service.Invalidate(appKind);
            overridden = await Service.GetLatestUpdateInfo(appKind, default);
            overridden.Should().NotBeNull();
        });

        // assert
        overridden!.VersionString.Should().Be("9.9.9");
    }

    // Private methods

    // isPlayGateEnabled keeps the Play dependency on, which every other test turns off by
    // clearing GoogleStoreId - otherwise each of them would have to script an Android probe
    private async Task<IDisposable> NewTestSettings(AppKind appKind, bool isPlayGateEnabled = false)
    {
        Probes.Probes.Clear();
        // The records have no TTL, so a rerun would otherwise see what the last run settled
        await Service.RemoveCachedStoreUpdateInfo(appKind, default);
        await Service.RemoveCachedStoreUpdateInfo(AppKind.Android, default);
        var settings = Settings;
        var restore = new SettingsBackup(settings);
        settings.IsEnabled = true;
        settings.RecheckPeriods = [TimeSpan.FromSeconds(1)];
        settings.AnnounceDelay = TimeSpan.Zero; // The tests that need one set their own
        settings.Overrides = ImmutableDictionary<string, string>.Empty;
        if (!isPlayGateEnabled && appKind != AppKind.Android)
            settings.GoogleStoreId = "";

        // The app host is shared, and the previous test's value outlives its record until the
        // invalidation this starts has been consolidated - so wait for the cleared state to show
        await WhenClear(appKind);
        if (isPlayGateEnabled)
            await WhenClear(AppKind.Android);

        return restore;
    }

    private Task WhenClear(AppKind appKind)
    {
        if (appKind == AppKind.Wasm)
            return Task.CompletedTask;

        return WhenPolled(async () => {
            Service.Invalidate(appKind);
            var info = await Service.GetLatestUpdateInfo(appKind, default);
            info.Should().BeNull();
        });
    }

    // ConsolidationDelay = 0 holds an invalidation back until the recomputed value actually differs,
    // so ComputedTest.When can't wait for a side effect or for a value that ends up unchanged
    private static async Task WhenPolled(Func<Task> assertion)
    {
        var startedAt = CpuTimestamp.Now;
        while (true) {
            try {
                await assertion.Invoke();
                return;
            }
            catch (Exception) when (startedAt.Elapsed < TestTimeout) {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private static string NewBuildBehindOwn(int buildOffset)
        => new Version(OwnVersion.Major, OwnVersion.Minor, Math.Max(OwnVersion.Build - buildOffset, 0))
            .ToString();

    // Nested types

    private sealed class SettingsBackup(AppUpdateSettings settings) : IDisposable
    {
        private readonly AppUpdateSettings _backup = new() {
            IsEnabled = settings.IsEnabled,
            GoogleStoreId = settings.GoogleStoreId,
            RecheckPeriods = settings.RecheckPeriods,
            AnnounceDelay = settings.AnnounceDelay,
            WasmGracePeriod = settings.WasmGracePeriod,
            Overrides = settings.Overrides,
        };
        private AppUpdateSettings Settings { get; } = settings;

        public void Dispose()
        {
            Settings.IsEnabled = _backup.IsEnabled;
            Settings.GoogleStoreId = _backup.GoogleStoreId;
            Settings.RecheckPeriods = _backup.RecheckPeriods;
            Settings.AnnounceDelay = _backup.AnnounceDelay;
            Settings.WasmGracePeriod = _backup.WasmGracePeriod;
            Settings.Overrides = _backup.Overrides;
        }
    }
}
