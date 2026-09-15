using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Transcription;
using ActualLab.Versioning;

namespace ActualChat.Streaming.Services;

// One of the two is set: Stored once the dub exists as media, Live while it's being synthesized -
// an AudioSource whose frames arrive as the synthesizer produces them
public sealed record ReplayDub(ActualChat.Media.Media? Stored, AudioSource? Live);

// A dub for replay is the stored translation of an entry spoken once and kept as media on that
// translation; it's made the first time a listener needs it and reused until the translation changes
public sealed class ReplayDubs(IServiceProvider services)
{
    private readonly ConcurrentDictionary<(ChatEntryId Id, Language Language), Task<ReplayDub?>> _inFlight = new();
    private readonly SemaphoreSlim _synthesisLimiter = new(Constants.Audio.ReplayDubMaxConcurrentSynthesis);
    private IServiceProvider Services { get; } = services;
    private IChatEntryLanguagesBackend EntryLanguages
        => field ??= Services.GetRequiredService<IChatEntryLanguagesBackend>();
    private ITranslationsBackend Translations => field ??= Services.GetRequiredService<ITranslationsBackend>();
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private ISpeechSynthesizer Synthesizer => field ??= Services.GetRequiredService<ISpeechSynthesizer>();
    private AudioSegmentSaver Saver => field ??= Services.GetRequiredService<AudioSegmentSaver>();
    private ICommander Commander => field ??= Services.Commander();
    private ILogger Log => field ??= Services.LogFor<ReplayDubs>();

    public async Task<ReplayDub?> GetOrCreate(ChatEntry entry, Language language, CancellationToken cancellationToken)
    {
        var key = (entry.Id, language);
        var source = TaskCompletionSourceExt.New<ReplayDub?>();
        var dubTask = _inFlight.GetOrAdd(key, source.Task);
        // Only the winner of the race starts the work, and only after it's published in the map -
        // a synchronously-completing Run() can then never race its own removal out of the map
        if (ReferenceEquals(dubTask, source.Task))
            _ = Run();
        try {
            return await dubTask.WaitAsync(Constants.Audio.ReplayDubTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            // The work keeps running on its own budget; a later call may still reuse its result
            return null;
        }

        async Task Run()
        {
            ReplayDub? result;
            // Independent of our own caller's token: a slow entry must keep synthesizing after
            // ReplayDubTimeout elapses above, bounded only by shutdown and ReplayDubSynthesisTimeout
            using var cts = Services.HostLifetime().CreateStopTokenSource();
            cts.CancelAfter(Constants.Audio.ReplayDubSynthesisTimeout);
            try {
                result = await GetOrCreateImpl(entry, language, source, cts.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e.IsCancellationOf(cts.Token)) {
                Log.LogInformation("GetOrCreate: {Language} dub for #{EntryId} timed out, {Outcome}",
                    language, entry.Id, FailureOutcome());
                result = null;
            }
            catch (Exception e) {
                Log.LogWarning(e, "GetOrCreate: {Language} dub for #{EntryId} failed, {Outcome}",
                    language, entry.Id, FailureOutcome());
                result = null;
            }
            // Only our own entry, and only now: a Live result is handed out mid-way, but the entry
            // stays until the dub is stored, so whoever finds no entry re-reads the stamped
            // translation and gets Stored. A fresh entry may already have taken the key
            _inFlight.TryRemove(new KeyValuePair<(ChatEntryId, Language), Task<ReplayDub?>>(key, source.Task));
            source.TrySetResult(result);
        }

        string FailureOutcome()
            => source.Task.IsCompletedSuccessfully
                ? "the dub streamed to its listeners won't be stored"
                : "serving the original";
    }

    // Internal for tests

    internal int InFlightCount => _inFlight.Count;

    // Private methods

    private async Task<ReplayDub?> GetOrCreateImpl(
        ChatEntry entry,
        Language language,
        TaskCompletionSource<ReplayDub?> liveResult,
        CancellationToken cancellationToken)
    {
        if (entry.Audio is not { } audio || audio.BlobId.IsNullOrEmpty() || !entry.SupportsTranslation(false))
            return null;
        if (await IsSpokenIn(entry, language, cancellationToken).ConfigureAwait(false))
            return null;

        var id = TranslationId.New(entry.Id, language);
        var translation = await WaitForTranslation(id, cancellationToken).ConfigureAwait(false);
        if (translation == null || translation.MatchesOriginal(entry.Content))
            return null;

        if (translation.HasValidDub()) {
            var existing = await MediaBackend.Get(translation.DubMediaId, cancellationToken).ConfigureAwait(false);
            if (existing != null)
                return new ReplayDub(existing, null);
            // The media is gone; fall through and make it again
        }

        var text = translation.Content;
        // The MediaId is generated up front so the blob path is unique per attempt: a raced-out
        // loser's cleanup below must never delete a blob another attempt is still using
        var mediaId = MediaId.New(entry.ChatId.Value);
        var blobId = BlobPath.Format(BlobScope.AudioRecord, mediaId.LocalId, $"{language.Value}.webm");
        AudioSource synthesized;
        // Caps concurrent Soniox REST calls; held only around the TTS + upload, not the wait above
        await _synthesisLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            synthesized = await Synthesizer.Synthesize(text, new SpeechSynthesisOptions(language), cancellationToken)
                .ConfigureAwait(false);
            // An AudioSource memoizes its frames, so every GetFrames replays them from the start:
            // the waiting callers play the dub as it's spoken while the saver stores the same frames
            liveResult.TrySetResult(new ReplayDub(null, synthesized));
            await Saver.SaveAndCreateMedia(synthesized, mediaId, blobId, cancellationToken).ConfigureAwait(false);
        }
        finally {
            _synthesisLimiter.Release();
        }
        if (synthesized.Duration <= TimeSpan.Zero) {
            // Nothing was actually spoken - the media just created is useless
            await Commander
                .Call(new MediaBackend_Change(mediaId, null, Change.Remove<MediaFull>()), true, cancellationToken)
                .ConfigureAwait(false);
            return null;
        }

        Translation? stamped;
        try {
            var diff = new TranslationDiff {
                DubMediaId = mediaId,
                DubContentHash = ChatEntryHashExt.GetContentHashString(text),
            };
            var translateChange = new TranslationsBackend_Change(id, translation.Version, Change.Update(diff));
            stamped = await Commander.Call(translateChange, true, cancellationToken).ConfigureAwait(false);
        }
        catch (VersionMismatchException) {
            stamped = null;
        }
        if (stamped?.DubMediaId != mediaId) {
            // A re-translation raced us: its content is newer than what we spoke
            await Commander
                .Call(new MediaBackend_Change(mediaId, null, Change.Remove<MediaFull>()), true, cancellationToken)
                .ConfigureAwait(false);
            return null;
        }

        var stored = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        return stored == null ? null : new ReplayDub(stored, null);
    }

    private async Task<bool> IsSpokenIn(ChatEntry entry, Language language, CancellationToken cancellationToken)
    {
        var idTile = Constants.Chat.EntryIdTiles.GetTile(entry.LocalId);
        var tile = await EntryLanguages.GetTile(entry.ChatId, idTile.Range, cancellationToken).ConfigureAwait(false);
        var entryLanguage = tile.Entries.FirstOrDefault(x => x.Id == entry.Id);
        return entryLanguage != null && entryLanguage.Languages.Any(x => x.IsoCode == language.IsoCode);
    }

    private async Task<Translation?> WaitForTranslation(TranslationId id, CancellationToken cancellationToken)
    {
        // Get(translateIfMissing: true) only enqueues the translation; OnChange invalidates the
        // compute method this depends on, so Computed.When wakes on the exact write instead of polling
        await Translations.Get(id, translateIfMissing: true, cancellationToken).ConfigureAwait(false);
        var computed = await Computed
            .Capture(() => Translations.Get(id, translateIfMissing: false, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        computed = await computed.When(t => t is { IsStreaming: false }, cancellationToken).ConfigureAwait(false);
        return computed.Value;
    }
}
