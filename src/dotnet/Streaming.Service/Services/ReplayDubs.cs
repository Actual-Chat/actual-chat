using ActualChat.Chat;
using ActualChat.Transcription;
using ActualLab.Versioning;

namespace ActualChat.Streaming.Services;

// A dub for replay is the stored translation of an entry spoken once and kept as media on that
// translation; it's made the first time a listener needs it and reused until the translation changes
public sealed class ReplayDubs(IServiceProvider services)
{
    private readonly ConcurrentDictionary<(ChatEntryId Id, Language Language), Task<ActualChat.Media.Media?>> _inFlight = new();
    private IServiceProvider Services { get; } = services;
    private IChatEntryLanguagesBackend EntryLanguages => field ??= Services.GetRequiredService<IChatEntryLanguagesBackend>();
    private ITranslationsBackend Translations => field ??= Services.GetRequiredService<ITranslationsBackend>();
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private ISpeechSynthesizer Synthesizer => field ??= Services.GetRequiredService<ISpeechSynthesizer>();
    private AudioSegmentSaver Saver => field ??= Services.GetRequiredService<AudioSegmentSaver>();
    private ICommander Commander => field ??= Services.Commander();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILogger Log => field ??= Services.LogFor<ReplayDubs>();

    public Task<ActualChat.Media.Media?> GetOrCreate(ChatEntry entry, Language language, CancellationToken cancellationToken)
    {
        var key = (entry.Id, language);
        // The first caller does the work; the others await the same task, and it's forgotten once done
        var task = _inFlight.GetOrAdd(key, _ => Run());
        return task.WaitAsync(cancellationToken);

        async Task<ActualChat.Media.Media?> Run()
        {
            try {
                using var cts = new CancellationTokenSource(Constants.Audio.ReplayDubTimeout);
                return await GetOrCreateImpl(entry, language, cts.Token).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogInformation(e, "GetOrCreate: no {Language} dub for #{EntryId}, serving the original",
                    language, entry.Id);
                return null;
            }
            finally {
                _inFlight.TryRemove(key, out _);
            }
        }
    }

    // Private methods

    private async Task<ActualChat.Media.Media?> GetOrCreateImpl(ChatEntry entry, Language language, CancellationToken cancellationToken)
    {
        if (entry.Audio is not { } audio || audio.BlobId.IsNullOrEmpty() || entry.Content.IsNullOrWhiteSpace())
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
                return existing;
            // The media is gone; fall through and make it again
        }

        var text = translation.Content;
        var synthesized = await Synthesizer.Synthesize(text, new SpeechSynthesisOptions(language), cancellationToken)
            .ConfigureAwait(false);
        var blobId = BlobPath.Format(BlobScope.AudioRecord, audio.StreamId.NullIfEmpty() ?? entry.Id.Value, $"{language.Value}.webm");
        var mediaId = await Saver.SaveAndCreateMedia(synthesized, blobId, entry.ChatId, cancellationToken).ConfigureAwait(false);
        Translation? stamped;
        try {
            stamped = await Commander.Call(new TranslationsBackend_Change(id, translation.Version, Change.Update(new TranslationDiff {
                DubMediaId = mediaId,
                DubContentHash = ChatEntryHashExt.GetContentHashString(text),
            })), true, cancellationToken).ConfigureAwait(false);
        }
        catch (VersionMismatchException) {
            stamped = null;
        }
        if (stamped?.DubMediaId != mediaId) {
            // A re-translation raced us: its content is newer than what we spoke
            await Commander.Call(new MediaBackend_Change(mediaId, null, Change.Remove<MediaFull>()), true, cancellationToken)
                .ConfigureAwait(false);
            return null;
        }

        return await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
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
        // Get(translateIfMissing: true) only enqueues the translation and returns what's there now;
        // the entry's translation isn't live, so waiting it out here is the only way to speak it
        var translation = await Translations.Get(id, translateIfMissing: true, cancellationToken).ConfigureAwait(false);
        while (translation == null || translation.IsStreaming) {
            await Clocks.CpuClock.Delay(Constants.Audio.DubTranslationRetryDelay, cancellationToken).ConfigureAwait(false);
            translation = await Translations.Get(id, translateIfMissing: false, cancellationToken).ConfigureAwait(false);
        }
        return translation;
    }
}
