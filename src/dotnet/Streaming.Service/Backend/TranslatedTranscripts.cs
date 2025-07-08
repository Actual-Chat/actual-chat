using ActualChat.Chat;
using ActualChat.Mesh;
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
    private MeshNode ThisNode => field ??= Services.MeshWatcher().ThisNode;
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Services.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.TranscriptionTranslation);

    private readonly StreamStore<TranslatedTranscriptDiff> _translatedTranscripts;

    public TranslatedTranscripts(IServiceProvider services)
    {
        Services = services;
        _translatedTranscripts = new (ThisNode.Ref) {
            Log = services.LogFor($"{GetType().FullName}.TranslatedTranscripts"),
            ExpirationDelay = TimeSpan.FromSeconds(30),
            ShareWaitDelay = TimeSpan.FromSeconds(5),
            OnStreamExpire = OnStreamExpire,
        };
        return;

        void OnStreamExpire(StreamId streamId)
        {
            _activePublishers.Remove(streamId, out var delegatingWorker);
            delegatingWorker?.Dispose();
        }
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
        var streamId = StreamId.New(originalStreamId, translationId.Language);
        var worker = _activePublishers.GetOrAdd(streamId, PublisherFactory, (this, translationId, originalStream));
        // since ValueFactory in concurrent dictionary can run concurrently we start only single one
        worker.Start();
        var stream = await _translatedTranscripts.Get(streamId, cancellationToken).ConfigureAwait(false);
        return stream?.Select(x => x.Translated);

        static DelegatingWorker PublisherFactory(StreamId streamId, (TranslatedTranscripts, TranslationId, IAsyncEnumerable<TranscriptDiff>) args)
        {
            var worker = DelegatingWorker.New(ct => {
                var (self, translationId, originalStream) = args;
                return self.Publish(translationId, streamId, originalStream, ct);
            }, start: false);
            return worker;
        }
    }

    // Private methods

    private async Task Publish(
        TranslationId translationId,
        StreamId streamId,
        IAsyncEnumerable<TranscriptDiff> originalStream,
        CancellationToken cancellationToken)
    {
        await CreateTranslation(translationId, streamId, cancellationToken).ConfigureAwait(false);
        var translationDiffs = Translate(translationId, originalStream, cancellationToken).Memoize(cancellationToken);
        await _translatedTranscripts.Publish(streamId, translationDiffs).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<TranslatedTranscriptDiff> Translate(
        TranslationId translationId,
        IAsyncEnumerable<TranscriptDiff> originalStream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var transcript = Transcript.Empty;
        var stableTranscript = Transcript.Empty;
        var stableTranslatedTranscript = Transcript.Empty;
        // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
        await foreach (var transcriptDiff in originalStream.ConfigureAwait(false)) {
            transcript += transcriptDiff;
            var prefix = stableTranscript.Text;
            // The diff represents the changes between the current transcript (transcript) and the stable transcript (stableTranscript).
            // This diff is distinct from the transcriptDiff object because transcriptDiff is derived from the unstable transcript, which may still be undergoing changes.
            // The purpose of this calculation is to isolate the differences that have occurred since the last stable state of the transcript.
            var diffSinceStable = transcript - stableTranscript;
            var content = diffSinceStable.TextDiff.Suffix ?? "";
            if (transcriptDiff.IsStable)
                stableTranscript = transcript;
            if (content.IsNullOrWhiteSpace())
                continue;

            var translatedContent = await TranslationsBackend.Translate(translationId, prefix, content, cancellationToken).ConfigureAwait(false);
            if (OrdinalIgnoreCaseEquals(translatedContent, Constants.Chat.NoTranslationNeededText))
                translatedContent = content; // No translation needed, use original content

            if (!translatedContent.StartsWith(" ", StringComparison.OrdinalIgnoreCase) && stableTranslatedTranscript.Text.Length > 0)
                translatedContent = $" {translatedContent}";

            var translatedTranscript = stableTranslatedTranscript.WithSuffix(translatedContent, diffSinceStable.TimeMapDiff.Suffix.Scale(content.Length, translatedContent.Length));
            var translatedDiff = translatedTranscript - stableTranslatedTranscript;
            yield return new (transcriptDiff, translatedDiff);
            if (transcriptDiff.IsStable)
                stableTranslatedTranscript = translatedTranscript with { IsStable = true };
        }
        // Update the final translation
        await FinalizeTranslation(translationId, stableTranslatedTranscript.Text, stableTranscript.Text, cancellationToken).ConfigureAwait(false);
    }

    private Task CreateTranslation(
        TranslationId translationId,
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("Creating streaming translation #{TranslationId}", translationId);
        var cmd = new TranslationsBackend_Change(translationId,
            null,
            Change.Create(new Translation(translationId) {
                StreamId = streamId.Value,
                Content = "",
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

    // Nested types

    private sealed record TranslatedTranscriptDiff(TranscriptDiff Original, TranscriptDiff Translated);
}
