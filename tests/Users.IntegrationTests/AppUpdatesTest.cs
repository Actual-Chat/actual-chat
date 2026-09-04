using ActualChat.Testing.Host;
using ActualChat.Users.AppStores;
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
    private AppUpdateStore Store => AppHost.Services.GetRequiredService<AppUpdateStore>();
    private AppUpdateProber Prober => AppHost.Services.GetRequiredService<AppUpdateProber>();
    private ScriptedStoreProbes Probes
        => (ScriptedStoreProbes)AppHost.Services.GetRequiredService<StoreProbes>();
    private AppUpdateSettings Settings
        => AppHost.Services.GetRequiredService<UsersSettings>().AppUpdates;

    [Fact]
    public async Task ShouldReportNothingUntilTheStorePublishesTheBuild()
    {
        // arrange
        const AppKind appKind = AppKind.Android;
        using var __ = await NewTestSettings(appKind);
        var probe = Probes.Script(appKind, NewResult("1.0.0"));

        // act
        var whileBehind = await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            probe.CallCount.Should().BeGreaterThan(0, "an unsettled kind must be probed");
            return info;
        }, TestTimeout);
        probe.Result = NewResult(OwnVersion.ToString());
        var published = await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            info.Should().NotBeNull();
            return info!;
        }, TestTimeout);
        var callCountWhenSettled = probe.CallCount;
        await Task.Delay(TimeSpan.FromSeconds(2));

        // assert
        whileBehind.Should().BeNull();
        published.Version.Should().Be(OwnVersion.ToString());
        published.AppKind.Should().Be(appKind);
        probe.CallCount.Should().Be(callCountWhenSettled, "a settled release is never probed again");
    }

    [Fact]
    public async Task ShouldReportTheAnnouncedReleaseWhileTheServerIsAhead()
    {
        // arrange
        const AppKind appKind = AppKind.Windows;
        using var __ = await NewTestSettings(appKind);
        var announcedAt = Clocks.SystemClock.Now - TimeSpan.FromDays(1);
        var announced = new AppUpdateInfo(appKind, "1.0.0", "1.0.0.0", announcedAt, announcedAt);
        await Store.Set(appKind,
            new AppUpdateRecord(announced, "1.0.0.0", announcedAt, announcedAt), default);
        Probes.Script(appKind, NewResult("1.0.0"));

        // act
        var info = await Service.GetLatestUpdateInfo(appKind, default);

        // assert
        info.Should().NotBeNull();
        info!.Version.Should().Be("1.0.0", "a client older than the announced release still needs a banner");
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
        await Store.Set(appKind,
            new AppUpdateRecord(announced, "1.0.0.0", announcedAt, announcedAt), default);
        Service.Invalidate(appKind);
        Probes.Script(appKind, NewResult(OwnVersion.ToString()));

        // act
        var whilePending = await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            var record = await Store.Get(appKind, ct);
            record!.Info!.Version.Should().Be(OwnVersion.ToString(), "the release must be detected first");
            return info;
        }, TestTimeout);
        var afterDelay = await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            info!.Version.Should().Be(OwnVersion.ToString());
            return info;
        }, TestTimeout);

        // assert
        whilePending!.Version.Should().Be("1.0.0", "a detected release is held back for AnnounceDelay");
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
        await Store.Set(appKind, new AppUpdateRecord(pending, "0.9.0", now, now, announced), default);
        Service.Invalidate(appKind);
        var probe = Probes.Script(appKind, NewResult("0.9.0"));

        // act
        var whilePending = await Service.GetLatestUpdateInfo(appKind, default);
        var afterDelay = await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            probe.CallCount.Should().BeGreaterThan(0, "probing resumes once the window is out");
            return info;
        }, TestTimeout);

        // assert
        whilePending!.Version.Should().Be("0.8.0");
        afterDelay!.Version.Should().Be("0.9.0");
    }

    [Fact]
    public async Task TrainOnlyStoreShouldAnnounceOnlyAfterTheStoreRecordChanges()
    {
        // arrange
        const AppKind appKind = AppKind.Ios;
        using var __ = await NewTestSettings(appKind);
        var ownTrain = $"{OwnVersion.Major.Format()}.{OwnVersion.Minor.Format()}";
        var releasedAt = Clocks.SystemClock.Now - TimeSpan.FromDays(30);
        // A two-part version can't be compared with a build version, so the first probe is a baseline
        var probe = Probes.Script(appKind, new StoreProbeResult(ownTrain, null, releasedAt));

        // act
        var afterBaseline = await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            probe.CallCount.Should().BeGreaterThan(0, "an unsettled kind must be probed");
            return info;
        }, TestTimeout);
        probe.Result = new StoreProbeResult(ownTrain, null, Clocks.SystemClock.Now);
        var announced = await ComputedTest.When(async ct => {
            var info = await Service.GetLatestUpdateInfo(appKind, ct);
            info.Should().NotBeNull();
            return info!;
        }, TestTimeout);

        // assert
        afterBaseline.Should().BeNull("the first sighting can't tell 2.17 from 2.17.100");
        announced.Version.Should().Be(OwnVersion.ToString());
        announced.StoreVersion.Should().Be(ownTrain);
    }

    [Fact]
    public async Task WasmShouldWaitOutTheRollingDeployGrace()
    {
        // arrange
        using var __ = await NewTestSettings(AppKind.Wasm);
        Settings.WasmGracePeriod = TimeSpan.FromHours(1);
        Service.Invalidate(AppKind.Wasm);

        // act
        var withinGrace = await Service.GetLatestUpdateInfo(AppKind.Wasm, default);
        Settings.WasmGracePeriod = TimeSpan.Zero;
        Service.Invalidate(AppKind.Wasm);
        var afterGrace = await Service.GetLatestUpdateInfo(AppKind.Wasm, default);

        // assert
        withinGrace.Should().BeNull();
        afterGrace.Should().NotBeNull();
        afterGrace!.Version.Should().Be(OwnVersion.ToString());
        afterGrace.StoreVersion.Should().Be(ApiConstants.FullVersionString);
    }

    [Fact]
    public async Task ShouldReportNothingOutsideProductionUnlessOverridden()
    {
        // arrange
        const AppKind appKind = AppKind.MacOS;
        using var __ = await NewTestSettings(appKind);
        Settings.IsEnabled = null; // i.e. production instances only, and a test host isn't one

        // act
        var disabled = await Service.GetLatestUpdateInfo(appKind, default);
        Settings.Overrides = new Dictionary<string, string> { { appKind.ToString(), "9.9.9" } };
        Service.Invalidate(appKind);
        var overridden = await Service.GetLatestUpdateInfo(appKind, default);

        // assert
        disabled.Should().BeNull();
        overridden.Should().NotBeNull();
        overridden!.Version.Should().Be("9.9.9");
    }

    // Private methods

    private async Task<IDisposable> NewTestSettings(AppKind appKind)
    {
        Probes.Probes.Clear();
        // A previous test that used this kind leaves a prober entry whose next attempt is
        // scheduled by the restored production backoff, i.e. up to half an hour out
        Prober.Forget(appKind);
        // The records have no TTL, so a rerun would otherwise see what the last run settled
        await Store.Remove(appKind, default);
        var settings = Settings;
        var restore = new SettingsBackup(settings);
        settings.IsEnabled = true;
        settings.RecheckPeriod = TimeSpan.FromSeconds(1);
        settings.ProbeDelayMin = 0.1;
        settings.ProbeDelayMax = 0.3;
        settings.MinProbeInterval = TimeSpan.FromMilliseconds(100);
        settings.AnnounceDelay = TimeSpan.Zero; // The tests that need one set their own
        settings.Overrides = ImmutableDictionary<string, string>.Empty;
        Service.Invalidate(appKind);
        return restore;
    }

    private static StoreProbeResult NewResult(string version)
        => new(version, VersionExt.ParseBuildVersion(version), null);

    // Nested types

    private sealed class SettingsBackup(AppUpdateSettings settings) : IDisposable
    {
        private readonly AppUpdateSettings _backup = new() {
            IsEnabled = settings.IsEnabled,
            RecheckPeriod = settings.RecheckPeriod,
            ProbeDelayMin = settings.ProbeDelayMin,
            ProbeDelayMax = settings.ProbeDelayMax,
            MinProbeInterval = settings.MinProbeInterval,
            AnnounceDelay = settings.AnnounceDelay,
            WasmGracePeriod = settings.WasmGracePeriod,
            Overrides = settings.Overrides,
        };
        private AppUpdateSettings Settings { get; } = settings;

        public void Dispose()
        {
            Settings.IsEnabled = _backup.IsEnabled;
            Settings.RecheckPeriod = _backup.RecheckPeriod;
            Settings.ProbeDelayMin = _backup.ProbeDelayMin;
            Settings.ProbeDelayMax = _backup.ProbeDelayMax;
            Settings.MinProbeInterval = _backup.MinProbeInterval;
            Settings.AnnounceDelay = _backup.AnnounceDelay;
            Settings.WasmGracePeriod = _backup.WasmGracePeriod;
            Settings.Overrides = _backup.Overrides;
        }
    }
}
