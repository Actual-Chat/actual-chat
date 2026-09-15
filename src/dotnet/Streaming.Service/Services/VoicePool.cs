using ActualChat.Hashing;
using ActualChat.Streaming.Module;
using ActualChat.Transcription;
using ActualChat.Users;
using ActualLab.Versioning;

namespace ActualChat.Streaming.Services;

/// <summary>
/// A transient pool of Soniox clones of opted-in speakers' voices: a clone is made the first time a
/// speaker's dub asks for it and kept while it's used (<see cref="VoicePoolSweeper"/> drops the idle
/// ones). Null means the stock voice: the pool is full, the attempt failed, or there's no Soniox here.
/// </summary>
public sealed class VoicePool(IServiceProvider services)
{
    public const string NamePrefix = "voxt-";
    private static readonly TimeSpan ReadyPollDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TouchPeriod = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<UserId, Task<string?>> _inFlight = new();

    private IServiceProvider Services { get; } = services;
    private StreamingSettings Settings { get; } = services.GetRequiredService<StreamingSettings>();
    // Null on hosts without a Soniox key: the pool is then simply unavailable
    private ISonioxVoices? SonioxVoices { get; } = services.GetService<ISonioxVoices>();
    private IUserVoicesBackend UserVoicesBackend => field ??= Services.GetRequiredService<IUserVoicesBackend>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private VoiceSampleBuilder SampleBuilder => field ??= Services.GetRequiredService<VoiceSampleBuilder>();
    private IBlobStorages Blobs => field ??= Services.GetRequiredService<IBlobStorages>();
    private ICommander Commander => field ??= Services.Commander();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public int Quota => Settings.SonioxVoiceQuota ?? Constants.Audio.VoiceCloneQuota;

    public async Task<string?> Acquire(UserId userId, CancellationToken cancellationToken)
    {
        if (SonioxVoices == null || userId.IsGuestOrNull())
            return null;

        var source = TaskCompletionSourceExt.New<string?>();
        var acquireTask = _inFlight.GetOrAdd(userId, source.Task);
        // Only the winner of the race starts the work, and only after it's published in the map -
        // a synchronously-completing Run() can then never race its own removal out of the map
        if (ReferenceEquals(acquireTask, source.Task))
            _ = Run();
        try {
            return await acquireTask
                .WaitAsync(Constants.Audio.VoiceCloneAcquireTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            // The work keeps running on its own budget; the clone serves the next utterance
            return null;
        }

        async Task Run()
        {
            string? result;
            // Independent of our own caller's token: a clone is worth finishing after the dub that asked
            // for it gave up, and a half-made one holds a quota slot until the sweeper gets to it
            using var cts = Services.HostLifetime().CreateStopTokenSource();
            try {
                result = await AcquireImpl(userId, cts.Token).ConfigureAwait(false);
            }
            catch (Exception e) {
                if (!e.IsCancellationOf(cts.Token))
                    Log.LogWarning(e, "Acquire: failed for {UserId}, using the stock voice", userId);
                result = null;
            }
            _inFlight.TryRemove(new KeyValuePair<UserId, Task<string?>>(userId, source.Task));
            source.TrySetResult(result);
        }
    }

    public async Task<bool> Release(UserVoice voice, CancellationToken cancellationToken)
    {
        // The record goes first: a concurrent Acquire that just touched it wins the version race
        // and keeps its clone, which would otherwise be deleted under a dub that's about to use it
        var diff = new UserVoiceDiff {
            Status = UserVoiceStatus.None,
            SonioxVoiceId = "",
            FailedUntil = Option.Some<Moment?>(null),
            ModifiedAt = Clocks.SystemClock.Now,
        };
        try {
            await Update(voice.UserId, voice, diff, cancellationToken).ConfigureAwait(false);
        }
        catch (VersionMismatchException) {
            Log.LogInformation("Release: {UserId}'s record changed under us, leaving it alone", voice.UserId);
            return false;
        }
        await DeleteSonioxVoice(voice.SonioxVoiceId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public static string NameOf(UserId userId, HashString sampleHash)
        => $"{NamePrefix}{userId.Value}-{VoiceSampleBuilder.ShortHashOf(sampleHash)}";

    public static bool IsOwnName(string name)
        => name.StartsWith(NamePrefix);

    // Internal for tests

    internal int InFlightCount => _inFlight.Count;

    // Private methods

    private async Task<string?> AcquireImpl(UserId userId, CancellationToken cancellationToken)
    {
        var settings = await ServerKvasBackend.ForUser(userId)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var voice = await UserVoicesBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (!settings.IsOwnVoiceEnabled) {
            if (voice is { Status: UserVoiceStatus.Ready or UserVoiceStatus.Creating })
                await Release(voice, cancellationToken).ConfigureAwait(false);
            return null;
        }
        if (voice is { Status: UserVoiceStatus.Failed, FailedUntil: { } failedUntil } && failedUntil > now) {
            Log.LogInformation("Acquire: {UserId}'s clone failed recently, no retry before {FailedUntil}",
                userId, failedUntil);
            return null;
        }
        if (voice is { Status: UserVoiceStatus.Creating }
            && voice.ModifiedAt + Constants.Audio.VoiceCloneCreatingTimeout > now) {
            // Another host is on it - this host's own attempts never overlap; an older one is a
            // crash leftover, and the version check below makes taking it over safe
            Log.LogInformation("Acquire: {UserId}'s clone is being made elsewhere", userId);
            return null;
        }

        string? createdVoiceId = null;
        try {
            // The builder is the one place that knows the current sample's hash; an unchanged sample
            // costs it a blob header read, not a rebuild
            var (sample, failure) = await SampleBuilder
                .Build(userId, settings, cancellationToken)
                .ConfigureAwait(false);
            if (sample == null) {
                Log.LogInformation("Acquire: no voice sample for {UserId} ({Failure})", userId, failure);
                if (voice is { Status: UserVoiceStatus.Ready or UserVoiceStatus.Creating })
                    await Release(voice, cancellationToken).ConfigureAwait(false);
                return null;
            }
            if (voice is { Status: UserVoiceStatus.Ready } && voice.SampleHash == sample.Hash) {
                await Touch(voice, now, cancellationToken).ConfigureAwait(false);
                return voice.SonioxVoiceId;
            }

            var activeVoices = await UserVoicesBackend.ListActive(cancellationToken).ConfigureAwait(false);
            var activeCount = activeVoices.Count(x => x.UserId != userId);
            if (activeCount >= Quota) {
                Log.LogInformation("Acquire: the pool is full ({Count}/{Quota}), {UserId} keeps the stock voice",
                    activeCount, Quota, userId);
                return null;
            }

            var oldVoiceId = voice?.SonioxVoiceId ?? "";
            var creatingDiff = new UserVoiceDiff {
                Status = UserVoiceStatus.Creating,
                SampleHash = sample.Hash,
                SonioxVoiceId = "",
                FailedUntil = Option.Some<Moment?>(null),
                LastUsedAt = now,
                CreatedAt = voice == null ? now : null,
                ModifiedAt = now,
            };
            voice = await Update(userId, voice, creatingDiff, cancellationToken).ConfigureAwait(false);
            // A hash mismatch: the clone of the previous sample goes before its replacement is made
            await DeleteSonioxVoice(oldVoiceId, cancellationToken).ConfigureAwait(false);
            var created = await CreateSonioxVoice(userId, sample, cancellationToken).ConfigureAwait(false);
            createdVoiceId = created.Id;
            // Stored right away, so the sweeper's reconcile sees it as ours while it's still cooking
            var createdDiff = new UserVoiceDiff { SonioxVoiceId = created.Id, ModifiedAt = Clocks.SystemClock.Now };
            voice = await Update(userId, voice, createdDiff, cancellationToken).ConfigureAwait(false);
            created = await WaitUntilReady(created, cancellationToken).ConfigureAwait(false);
            now = Clocks.SystemClock.Now;
            var readyDiff = new UserVoiceDiff { Status = UserVoiceStatus.Ready, LastUsedAt = now, ModifiedAt = now };
            await Update(userId, voice, readyDiff, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Acquire: cloned {UserId}'s voice as {VoiceId}", userId, created.Id);
            return created.Id;
        }
        catch (VersionMismatchException) {
            // Another host or the sweeper changed the record under us; whatever it decided stands
            Log.LogInformation("Acquire: {UserId}'s record changed under us, using the stock voice", userId);
            await DeleteSonioxVoice(createdVoiceId, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            var cooldown = Constants.Audio.VoiceCloneFailureCooldown;
            Log.LogWarning(e, "Acquire: couldn't clone {UserId}'s voice, using the stock voice for {Cooldown}",
                userId, cooldown);
            await DeleteSonioxVoice(createdVoiceId, cancellationToken).ConfigureAwait(false);
            now = Clocks.SystemClock.Now;
            var failedDiff = new UserVoiceDiff {
                Status = UserVoiceStatus.Failed,
                SonioxVoiceId = "",
                FailedUntil = Option.Some<Moment?>(now + cooldown),
                CreatedAt = voice == null ? now : null,
                ModifiedAt = now,
            };
            // A Ready record whose sample failed to build still points at the previous clone
            var staleVoiceId = voice?.SonioxVoiceId;
            try {
                await Update(userId, voice, failedDiff, cancellationToken).ConfigureAwait(false);
                if (staleVoiceId != createdVoiceId)
                    await DeleteSonioxVoice(staleVoiceId, cancellationToken).ConfigureAwait(false);
            }
            catch (VersionMismatchException) {
                Log.LogInformation("Acquire: {UserId}'s record changed under us, the failure isn't recorded", userId);
            }
            return null;
        }
    }

    private async Task Touch(UserVoice voice, Moment now, CancellationToken cancellationToken)
    {
        // Once a minute is precise enough for a 10-minute idle timeout and spares a write per utterance
        if (voice.LastUsedAt + TouchPeriod > now)
            return;

        try {
            var diff = new UserVoiceDiff { LastUsedAt = now, ModifiedAt = now };
            await Update(voice.UserId, voice, diff, cancellationToken).ConfigureAwait(false);
        }
        catch (VersionMismatchException) {
            // Someone else touched or reset it in between; the next utterance re-reads the record
        }
    }

    private async Task<SonioxVoice> CreateSonioxVoice(
        UserId userId,
        VoiceSample sample,
        CancellationToken cancellationToken)
    {
        var wav = await Blobs[BlobScope.AudioRecord].Read(sample.BlobId, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.NotFound<VoiceSample>($"The voice sample blob '{sample.BlobId}' is missing.");
        await using var _ = wav.ConfigureAwait(false);
        return await SonioxVoices!.Create(NameOf(userId, sample.Hash), wav, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SonioxVoice> WaitUntilReady(SonioxVoice voice, CancellationToken cancellationToken)
    {
        var clock = Clocks.CpuClock;
        var deadline = clock.Now + Constants.Audio.VoiceCloneReadyTimeout;
        while (!voice.IsReady) {
            // The client doesn't expose Soniox's reason, so IsFailed is all there is to report
            if (voice.IsFailed)
                throw StandardError.External($"Soniox reported voice '{voice.Id}' as failed.");
            if (clock.Now >= deadline)
                throw StandardError.Timeout($"Waiting for Soniox voice '{voice.Id}' to become ready");

            await clock.Delay(ReadyPollDelay, cancellationToken).ConfigureAwait(false);
            voice = await SonioxVoices!.Get(voice.Id, cancellationToken).ConfigureAwait(false)
                ?? throw StandardError.External($"Soniox voice '{voice.Id}' disappeared while being made.");
        }
        return voice;
    }

    private async Task DeleteSonioxVoice(string? voiceId, CancellationToken cancellationToken)
    {
        if (voiceId.IsNullOrEmpty())
            return;

        try {
            await SonioxVoices!.Delete(voiceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            // The record no longer points at it, so the sweeper's reconcile picks it up as an orphan
            Log.LogWarning(e, "Couldn't delete Soniox voice {VoiceId}", voiceId);
        }
    }

    private async Task<UserVoice> Update(
        UserId userId,
        UserVoice? voice,
        UserVoiceDiff diff,
        CancellationToken cancellationToken)
    {
        var change = voice == null ? Change.Create(diff) : Change.Update(diff);
        var command = new UserVoicesBackend_Change(userId, voice?.Version, change);
        var result = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.Internal("UserVoicesBackend_Change returned no record.");
        // A create finding an existing record returns it untouched - the same race as a stale version
        if (voice == null && diff.Status is { } status && result.Status != status)
            throw new VersionMismatchException($"User voice record for {userId} was created concurrently.");

        return result;
    }
}
