using ActualChat.Hashing;
using ActualChat.Streaming.Module;
using ActualChat.Transcription;
using ActualChat.Users;
using ActualLab.Versioning;

namespace ActualChat.Streaming.Services;

/// <summary>
/// A transient pool of Soniox clones of opted-in speakers' voices: a clone is made the first time a
/// speaker's dub asks for it and kept while it's used (<see cref="VoicePoolSweeper"/> drops the idle
/// ones). Null means the stock voice: the clone is still being made, the pool is full, the attempt
/// failed, or there's no Soniox here.
/// </summary>
public sealed class VoicePool(IServiceProvider services)
{
    private static readonly TimeSpan ReadyPollDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TouchPeriod = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<UserId, Task> _inFlight = new();

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

    // Every environment shares the Soniox project, so the names carry which one a voice belongs to
    // and the reconcile never touches another environment's clones
    public string NamePrefix { get; } = $"voxt-{EnvironmentOf(services.HostInfo())}-";
    public int Quota => Settings.SonioxVoiceQuota ?? Constants.Audio.VoiceCloneQuota;

    public async Task<string?> Acquire(UserId userId, CancellationToken cancellationToken)
    {
        // Never waits: a clone that isn't ready is made in the background and this dub speaks with
        // the stock voice, so the answer costs a settings read, a record read and the sample hash
        if (SonioxVoices == null || userId.IsGuestOrNull())
            return null;

        var settings = await ServerKvasBackend.ForUser(userId)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var voice = await UserVoicesBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (!settings.IsOwnVoiceEnabled) {
            if (voice is { Status: UserVoiceStatus.Ready or UserVoiceStatus.Creating })
                Start(userId, ct => Release(voice, ct));
            return null;
        }
        if (voice is { Status: UserVoiceStatus.Failed, FailedUntil: { } failedUntil } && failedUntil > now) {
            Log.LogDebug("Acquire: {UserId}'s clone failed recently, no retry before {FailedUntil}",
                userId, failedUntil);
            return null;
        }
        if (voice is { Status: UserVoiceStatus.Creating }
            && voice.ModifiedAt + Constants.Audio.VoiceCloneCreatingTimeout > now) {
            // This host's own attempt or another host's - own attempts never overlap; an older one is
            // a crash leftover, and the version check in Create makes taking it over safe
            Log.LogDebug("Acquire: {UserId}'s clone is still being made", userId);
            return null;
        }

        // The hash alone decides whether the clone at hand is the right one: the common Ready path
        // then costs no blob read, and nothing is built for a full pool either
        var hash = await SampleBuilder.GetHash(userId, settings, cancellationToken).ConfigureAwait(false);
        if (hash is not { } sampleHash) {
            Log.LogDebug("Acquire: no voice sample for {UserId}", userId);
            return null;
        }
        if (voice is { Status: UserVoiceStatus.Ready } && voice.SampleHash == sampleHash)
            return await Touch(voice, now, cancellationToken).ConfigureAwait(false);

        if (Start(userId, ct => MakeClone(userId, voice, settings, ct)))
            Log.LogInformation("Acquire: making {UserId}'s clone, this dub uses the stock voice", userId);
        else
            Log.LogDebug("Acquire: {UserId}'s clone is already being made", userId);
        return null;
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

        // The sample goes before the voice: an Acquire elsewhere that found the sample a moment ago
        // rebuilds it when its read comes up empty, and the Soniox call shouldn't widen that window
        await DeleteSample(voice.UserId, voice.SampleHash, cancellationToken).ConfigureAwait(false);
        await DeleteSonioxVoice(voice.SonioxVoiceId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public string NameOf(UserId userId, HashString sampleHash)
        => $"{NamePrefix}{userId.Value}-{VoiceSampleBuilder.ShortHashOf(sampleHash)}";

    public bool IsOwnName(string name)
        => name.StartsWith(NamePrefix);

    // Protected/internal methods

    internal int InFlightCount => _inFlight.Count;

    // Private methods

    private bool Start(UserId userId, Func<CancellationToken, Task> work)
    {
        // Single-flight per user: only the winner of the race starts the work, and only after it's
        // published in the map - a synchronously-completing Run() can then never race its own
        // removal out of the map
        var source = TaskCompletionSourceExt.New();
        if (!_inFlight.TryAdd(userId, source.Task))
            return false;

        _ = Run();
        return true;

        async Task Run()
        {
            try {
                // Independent of the caller's token: a clone is worth finishing after the dub that
                // asked for it is over, and a half-made one holds a quota slot until the sweeper gets to it
                using var cts = Services.HostLifetime().CreateStopTokenSource();
                try {
                    await work.Invoke(cts.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (!e.IsCancellationOf(cts.Token)) {
                    Log.LogWarning(e, "Acquire: failed for {UserId}, using the stock voice", userId);
                }
            }
            catch {
                // Cancelled by the host stop, or the token source itself couldn't be made
            }
            finally {
                _inFlight.TryRemove(new KeyValuePair<UserId, Task>(userId, source.Task));
                source.TrySetResult();
            }
        }
    }

    private async Task MakeClone(
        UserId userId,
        UserVoice? voice,
        UserLanguageSettings settings,
        CancellationToken cancellationToken)
    {
        var activeVoices = await UserVoicesBackend.ListActive(cancellationToken).ConfigureAwait(false);
        var activeCount = activeVoices.Count(x => x.UserId != userId);
        if (activeCount >= Quota) {
            Log.LogDebug("Acquire: the pool is full ({Count}/{Quota}), {UserId} keeps the stock voice",
                activeCount, Quota, userId);
            return;
        }

        var sample = await BuildSample(userId, voice, settings, cancellationToken).ConfigureAwait(false);
        if (sample == null)
            return;

        await Create(userId, voice, sample, settings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<VoiceSample?> BuildSample(
        UserId userId,
        UserVoice? voice,
        UserLanguageSettings settings,
        CancellationToken cancellationToken)
    {
        // A sample that can't be built right now says nothing about a clone made from an earlier
        // one, so a Ready record is left as it is
        try {
            var (sample, failure) = await SampleBuilder
                .Build(userId, settings, cancellationToken)
                .ConfigureAwait(false);
            if (sample != null)
                return sample;

            Log.LogDebug("Acquire: no voice sample for {UserId} ({Failure})", userId, failure);
            return null;
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Acquire: couldn't build {UserId}'s voice sample, using the stock voice", userId);
            if (voice is not { Status: UserVoiceStatus.Ready })
                await MarkFailed(userId, voice, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    private async Task<string?> Touch(UserVoice voice, Moment now, CancellationToken cancellationToken)
    {
        // Once a minute is precise enough for a 10-minute idle timeout and spares a write per utterance
        if (voice.LastUsedAt + TouchPeriod > now)
            return voice.SonioxVoiceId;

        try {
            var diff = new UserVoiceDiff { LastUsedAt = now, ModifiedAt = now };
            voice = await Update(voice.UserId, voice, diff, cancellationToken).ConfigureAwait(false);
        }
        catch (VersionMismatchException) {
            // A sweep may have just released the clone; only a record that's still Ready has one
            var current = await UserVoicesBackend.Get(voice.UserId, cancellationToken).ConfigureAwait(false);
            if (current is not { Status: UserVoiceStatus.Ready } || current.SampleHash != voice.SampleHash)
                return null;

            voice = current;
        }
        return voice.SonioxVoiceId;
    }

    private async Task<string?> Create(
        UserId userId,
        UserVoice? voice,
        VoiceSample sample,
        UserLanguageSettings settings,
        CancellationToken cancellationToken)
    {
        var now = Clocks.SystemClock.Now;
        string? createdVoiceId = null;
        try {
            var oldVoiceId = voice?.SonioxVoiceId ?? "";
            var oldSampleHash = voice?.SampleHash ?? HashString.None;
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
            // A hash mismatch: the clone of the previous sample goes before its replacement is made,
            // and so does that sample
            await DeleteSonioxVoice(oldVoiceId, cancellationToken).ConfigureAwait(false);
            if (oldSampleHash != sample.Hash)
                await DeleteSample(userId, oldSampleHash, cancellationToken).ConfigureAwait(false);
            var created = await CreateSonioxVoice(userId, sample, settings, cancellationToken).ConfigureAwait(false);
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
            Log.LogWarning(e, "Acquire: couldn't clone {UserId}'s voice, using the stock voice for {Cooldown}",
                userId, Constants.Audio.VoiceCloneFailureCooldown);
            // Only this attempt's voice is ours to delete here; a record that never reached Creating
            // still points at a live clone, which a failed attempt at a new one mustn't cost the speaker
            await DeleteSonioxVoice(createdVoiceId, cancellationToken).ConfigureAwait(false);
            if (voice is { Status: UserVoiceStatus.Creating })
                await MarkFailed(userId, voice, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    private async Task MarkFailed(UserId userId, UserVoice? voice, CancellationToken cancellationToken)
    {
        var now = Clocks.SystemClock.Now;
        var diff = new UserVoiceDiff {
            Status = UserVoiceStatus.Failed,
            SonioxVoiceId = "",
            FailedUntil = Option.Some<Moment?>(now + Constants.Audio.VoiceCloneFailureCooldown),
            CreatedAt = voice == null ? now : null,
            ModifiedAt = now,
        };
        try {
            await Update(userId, voice, diff, cancellationToken).ConfigureAwait(false);
        }
        catch (VersionMismatchException) {
            Log.LogInformation("Acquire: {UserId}'s record changed under us, the failure isn't recorded", userId);
            return;
        }

        // A sample exists only next to a Creating or Ready record: the retry after the cooldown rebuilds it
        if (voice != null)
            await DeleteSample(userId, voice.SampleHash, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SonioxVoice> CreateSonioxVoice(
        UserId userId,
        VoiceSample sample,
        UserLanguageSettings settings,
        CancellationToken cancellationToken)
    {
        var name = NameOf(userId, sample.Hash);
        try {
            return await CreateSonioxVoice(name, sample.BlobId, cancellationToken).ConfigureAwait(false);
        }
        catch (NotFoundException<VoiceSample> e) {
            // A Release on another host deleted the sample after the builder found it, along with the
            // record it went with - so it's rebuilt here rather than counted as a failed clone
            Log.LogInformation(e, "Acquire: {UserId}'s voice sample vanished while being cloned, rebuilding it",
                userId);
            var (rebuilt, failure) = await SampleBuilder
                .Build(userId, settings, cancellationToken)
                .ConfigureAwait(false);
            if (rebuilt == null)
                throw StandardError.NotFound<VoiceSample>($"The voice sample can't be rebuilt: {failure}.");
            if (rebuilt.Hash != sample.Hash)
                throw StandardError.NotFound<VoiceSample>("The voice sample changed while being cloned.");

            return await CreateSonioxVoice(name, rebuilt.BlobId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SonioxVoice> CreateSonioxVoice(string name, string blobId, CancellationToken cancellationToken)
    {
        try {
            return await CreateNamed().ConfigureAwait(false);
        }
        catch (Exception e) when (e is not NotFoundException<VoiceSample> && !e.IsCancellationOf(cancellationToken)) {
            // Names are unique per project, so a leftover of an earlier attempt - a delete that failed,
            // a host that died - rejects every retry until it's gone; the reconcile would get to it
            // eventually, but the speaker is waiting now
            var sonioxVoices = await SonioxVoices!.List(cancellationToken).ConfigureAwait(false);
            var sameNamed = sonioxVoices.FirstOrDefault(x => x.Name == name);
            if (sameNamed == null)
                throw;

            Log.LogWarning(e, "Acquire: Soniox voice '{Name}' already exists as {VoiceId}, replacing it",
                name, sameNamed.Id);
            await SonioxVoices.Delete(sameNamed.Id, cancellationToken).ConfigureAwait(false);
            return await CreateNamed().ConfigureAwait(false);
        }

        async Task<SonioxVoice> CreateNamed()
        {
            // Opened per attempt: the client reads the stream, so a retry can't reuse it
            var wav = await Blobs[BlobScope.AudioRecord].Read(blobId, cancellationToken).ConfigureAwait(false)
                ?? throw StandardError.NotFound<VoiceSample>($"The voice sample blob '{blobId}' is missing.");
            await using var _ = wav.ConfigureAwait(false);
            return await SonioxVoices!.Create(name, wav, cancellationToken).ConfigureAwait(false);
        }
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

    private async Task DeleteSample(UserId userId, HashString sampleHash, CancellationToken cancellationToken)
    {
        // The speaker's recording goes with the clone made from it; a blob that's already gone is fine
        if (sampleHash.IsNone)
            return;

        var blobId = VoiceSampleBuilder.BlobIdOf(userId, sampleHash);
        try {
            var blobs = Blobs[BlobScope.AudioRecord];
            if (await blobs.Exists(blobId, cancellationToken).ConfigureAwait(false))
                await blobs.Delete(blobId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Couldn't delete voice sample {BlobId}", blobId);
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

    private static string EnvironmentOf(HostInfo hostInfo)
    {
        if (hostInfo.IsTested)
            return "test";

        return hostInfo.BaseUrlKind switch {
            BaseUrlKind.Production => "prod",
            BaseUrlKind.Development => "dev",
            BaseUrlKind.Local => "local",
            _ => "unknown",
        };
    }
}
