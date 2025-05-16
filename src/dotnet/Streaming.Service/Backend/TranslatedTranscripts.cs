using ActualChat.Chat;
using ActualChat.Streaming.Services;
using ActualChat.Transcription;
using ActualLab.Diagnostics;

namespace ActualChat.Streaming;

public class TranslatedTranscripts : ProcessorBase
{
    private readonly ConcurrentDictionary<StreamId, DelegatingWorker> _activePublishers = new ();

    private IServiceProvider Services { get; }
    [field: AllowNull, MaybeNull]
    private ITranslationsBackend TranslationsBackend => field ??= Services.GetRequiredService<ITranslationsBackend>();
    [field: AllowNull, MaybeNull]
    private ICommander Commander => field ??= Services.Commander();
    [field: AllowNull, MaybeNull]
    private MomentClockSet Clocks => field ??= Services.Clocks();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Services.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.TranscriptionTranslation);

    private readonly StreamStore<TranslatedTranscriptDiff> _translatedTranscripts;

    public TranslatedTranscripts(IServiceProvider services)
    {
        Services = services;
        _translatedTranscripts = new () {
            Log = services.LogFor($"{GetType().FullName}.TranslatedTranscripts"),
        };
    }

    protected override async Task DisposeAsyncCore()
    {
        var streamIds = _activePublishers.Keys.ToList();
        foreach (var streamId in streamIds)
            if (_activePublishers.Remove(streamId, out var worker))
                await worker.DisposeSilentlyAsync().ConfigureAwait(false);
        await _translatedTranscripts.DisposeSilentlyAsync().ConfigureAwait(false);
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    public async Task<IAsyncEnumerable<TranscriptDiff>?> Get(
        TranslationId translationId,
        StreamId originalStreamId,
        IAsyncEnumerable<TranscriptDiff> originalStream,
        CancellationToken cancellationToken)
    {
        var streamId = GetTranslatedStreamId(originalStreamId, translationId.Language);

        var worker = _activePublishers.GetOrAdd(streamId, PublisherFactory);
        // since ValueFactory in concurrent dictionary can run concurrently we start only single one
        worker.Start();
        var stream = await _translatedTranscripts.Get(streamId, cancellationToken).ConfigureAwait(false);
        return stream?.Select(x => x.Translated);

        DelegatingWorker PublisherFactory(StreamId id)
            => DelegatingWorker.New(PublishTaskFactory, start: false);

        Task PublishTaskFactory(CancellationToken cancellationToken1)
            // ReSharper disable once PossibleMultipleEnumeration
            => Publish(translationId, streamId, originalStream, cancellationToken1);
    }

    private async Task Publish(
        TranslationId translationId,
        StreamId streamId,
        IAsyncEnumerable<TranscriptDiff> originalStream,
        CancellationToken cancellationToken)
    {
        var translationDiffs = Translate(translationId, originalStream, cancellationToken)
            .Throttle(Constants.Transcription.ThrottlePeriod, Clocks.CpuClock, cancellationToken)
            .Memoize(cancellationToken);
        var publishTask = _translatedTranscripts.Publish(streamId, translationDiffs);

        var translatedTranscripts = translationDiffs.Replay(cancellationToken);
        var last = Transcript.Empty;
        var lastOriginal = Transcript.Empty;
        Translation? translation = null;
        try {
            await foreach (var translatedTranscriptDiff in translatedTranscripts.ConfigureAwait(false)) {
                last += translatedTranscriptDiff.Translated;
                lastOriginal += translatedTranscriptDiff.Original;
                if (translation is not null || last.Text.IsNullOrWhiteSpace())
                    continue;

                await CreateTranslation(translationId, streamId, last.Text, cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            await FinalizeTranslation(translationId, last.Text, lastOriginal.Text, cancellationToken).ConfigureAwait(false);
        }
        await publishTask.ConfigureAwait(false);
    }

    private async IAsyncEnumerable<TranslatedTranscriptDiff> Translate(
        TranslationId translationId,
        IAsyncEnumerable<TranscriptDiff> originalStream,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        var translatedTranscript = Transcript.Empty;
        var originalTranscript = Transcript.Empty;
        await foreach (var originalTranscriptDiff in originalStream.Where(x => !x.TextDiff.IsNone).WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var prev = translatedTranscript;
            originalTranscript += originalTranscriptDiff;
            DebugLog?.LogDebug("Translating transcript for #{TranslationId}", translationId);
            var translatedContent = await TranslationsBackend.GetRealtime(translationId, originalTranscript.Text, cancellationToken).ConfigureAwait(false);
            var translatedTimeMap = originalTranscript.TimeMap.Scale(originalTranscript.Length, translatedContent.Length);
            translatedTranscript = new Transcript(translatedContent, translatedTimeMap, [translationId.Language]);
            yield return new (originalTranscriptDiff, TranscriptDiff.New(translatedTranscript, prev));
        }
    }

    private static StreamId GetTranslatedStreamId(StreamId streamId, Language language)
        => new (streamId.NodeRef, $"{streamId.LocalId}{language.Value.OrdinalReplace("-", "")}");

    private Task CreateTranslation(
        TranslationId translationId,
        StreamId streamId,
        string translatedText,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("Creating streaming translation #{TranslationId}", translationId);
        var cmd = new TranslationsBackend_Change(translationId,
            null,
            Change.Create(new Translation(translationId) {
                StreamId = streamId,
                Content = translatedText,
                TargetLanguage = translationId.Language,
            }));
        return Commander.Call(cmd, cancellationToken)
            .Catch(Log, "Failed to create streaming translation #{TranslationId}", translationId);
    }

    private Task FinalizeTranslation(
        TranslationId translationId,
        string translatedText,
        string originalText,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("Finalizing streaming translation #{TranslationId}", translationId);
        var cmd = new TranslationsBackend_Change(translationId,
            null,
            Change.Update(new Translation(translationId) {
                StreamId = Symbol.Empty,
                Content = translatedText,
                TargetLanguage = translationId.Language,
                SourceContentHash = ChatEntryHashExt.GetContentHashString(originalText),
            }));
        return Commander.Call(cmd, cancellationToken)
            .Catch(Log, "Failed to finalize streaming translation #{TranslationId}", translationId);
    }

    private sealed record TranslatedTranscriptDiff(TranscriptDiff Original, TranscriptDiff Translated);
}
