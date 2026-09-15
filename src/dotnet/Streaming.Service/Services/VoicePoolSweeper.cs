using ActualChat.Transcription;
using ActualChat.Users;

namespace ActualChat.Streaming.Services;

/// <summary>
/// The <see cref="VoicePool"/>'s release: every minute it drops the clones nobody dubbed with for
/// <see cref="Constants.Audio.VoiceCloneIdleTimeout"/>, those of speakers who opted out, and stale
/// Creating records; on deployed hosts its first pass also reconciles them with what Soniox holds.
/// </summary>
public sealed class VoicePoolSweeper(IServiceProvider services) : WorkerBase
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(TimeSpan.FromSeconds(5), Period);

    private IServiceProvider Services { get; } = services;
    private VoicePool Pool { get; } = services.GetRequiredService<VoicePool>();
    // Null on hosts without a Soniox key: there's nothing to sweep then
    private ISonioxVoices? SonioxVoices { get; } = services.GetService<ISonioxVoices>();
    private IUserVoicesBackend UserVoicesBackend => field ??= Services.GetRequiredService<IUserVoicesBackend>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<VoicePoolSweeper>();

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        // The reconcile deletes every voxt-* voice this host's DB doesn't know, so it must run only
        // where the DB is the deployment's own: a local server or a test host holding the dev key
        // would otherwise wipe the clones the dev pods are dubbing with
        var mustReconcile = Services.HostInfo().BaseUrlKind is BaseUrlKind.Production or BaseUrlKind.Development;
        return AsyncChain.From(Cycle)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelays, Clocks.CpuClock, Log)
            .CycleForever()
            .Run(cancellationToken);

        async Task Cycle(CancellationToken ct)
        {
            await SweepOnce(ct, mustReconcile).ConfigureAwait(false);
            mustReconcile = false;
            await Clocks.CpuClock.Delay(Period, ct).ConfigureAwait(false);
        }
    }

    // It's internal so tests can run a single pass without the loop's delay
    internal async Task SweepOnce(CancellationToken cancellationToken, bool mustReconcile = true)
    {
        if (SonioxVoices == null)
            return;

        var activeVoices = await UserVoicesBackend.ListActive(cancellationToken).ConfigureAwait(false);
        if (mustReconcile)
            activeVoices = await Reconcile(activeVoices, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        foreach (var voice in activeVoices) {
            var reason = await GetReleaseReason(voice, now, cancellationToken).ConfigureAwait(false);
            if (reason == null)
                continue;

            Log.LogInformation("Sweep: releasing {UserId}'s clone {VoiceId}: {Reason}",
                voice.UserId, voice.SonioxVoiceId, reason);
            await Pool.Release(voice, cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private async Task<ApiArray<UserVoice>> Reconcile(
        ApiArray<UserVoice> activeVoices,
        CancellationToken cancellationToken)
    {
        var sonioxVoices = await SonioxVoices!.List(cancellationToken).ConfigureAwait(false);
        var sonioxVoiceIds = sonioxVoices.Select(x => x.Id).ToHashSet();
        var referencedIds = activeVoices.Select(x => x.SonioxVoiceId).Where(x => !x.IsNullOrEmpty()).ToHashSet();
        // Only our own names: whatever else the organization holds isn't the pool's to delete
        var orphans = sonioxVoices.Where(x => VoicePool.IsOwnName(x.Name) && !referencedIds.Contains(x.Id)).ToList();
        foreach (var orphan in orphans) {
            Log.LogInformation("Reconcile: deleting orphaned Soniox voice {VoiceId} ({Name})", orphan.Id, orphan.Name);
            await SonioxVoices.Delete(orphan.Id, cancellationToken).ConfigureAwait(false);
        }

        var kept = new List<UserVoice>();
        foreach (var voice in activeVoices) {
            var isGone = voice.Status == UserVoiceStatus.Ready && !sonioxVoiceIds.Contains(voice.SonioxVoiceId);
            if (!isGone) {
                kept.Add(voice);
                continue;
            }

            Log.LogInformation("Reconcile: {UserId}'s clone {VoiceId} is gone at Soniox, resetting the record",
                voice.UserId, voice.SonioxVoiceId);
            await Pool.Release(voice, cancellationToken).ConfigureAwait(false);
        }
        return kept.ToApiArray();
    }

    private async Task<string?> GetReleaseReason(UserVoice voice, Moment now, CancellationToken cancellationToken)
    {
        if (voice.Status == UserVoiceStatus.Creating)
            return voice.ModifiedAt + Constants.Audio.VoiceCloneCreatingTimeout <= now
                ? "its creation never finished"
                : null;
        if (voice.LastUsedAt + Constants.Audio.VoiceCloneIdleTimeout <= now)
            return "idle";

        var settings = await ServerKvasBackend.ForUser(voice.UserId)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        return settings.IsOwnVoiceEnabled ? null : "the speaker opted out";
    }
}
