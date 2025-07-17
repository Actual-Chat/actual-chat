using ActualChat.Chat;
using ActualChat.Mesh;
using ActualChat.Queues;
using ActualChat.Streaming.Services;
using ActualChat.Transcription;
using ActualLab.Diagnostics;

namespace ActualChat.Streaming;

public class TranslatedTranscripts : ProcessorBase
{
    private static readonly TimeSpan TranslateThrottleDelay = TimeSpan.FromMilliseconds(500);
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
    private IQueues Queues => field ??= Services.Queues();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Services.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.TranscriptionTranslation);

    private readonly StreamStore<TranscriptDiff,TextEntryId> _translatedTranscripts;

    public TranslatedTranscripts(IServiceProvider services)
    {
        Services = services;
        _translatedTranscripts = new () {
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
        // Starting only indeed inserted worker
        worker.Start();
        return await _translatedTranscripts.Get(streamId, cancellationToken).ConfigureAwait(false);

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
        await _translatedTranscripts.Publish(streamId, null, translationDiffs).ConfigureAwait(false);
    }

    private IAsyncEnumerable<TranscriptDiff> Translate(
        TranslationId translationId,
        IAsyncEnumerable<TranscriptDiff> originalStream,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<TranscriptDiff>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });
        var reader = channel.Reader;
        var writer = channel.Writer;
        _ = Task.Run(async () => {
            Exception? error = null;
            var lastTranscript = Transcript.Empty;
            var stableTranscript = Transcript.Empty;
            var stableTranslatedTranscript = Transcript.Empty;
            try {
                // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
                await foreach (var transcriptDiffBatch in originalStream.Buffer(TranslateThrottleDelay, Clocks.CpuClock, cancellationToken: cancellationToken).ConfigureAwait(false)) {
                    if (transcriptDiffBatch.Count == 0)
                        continue; // Skip empty batches

                    var transcriptBatch = transcriptDiffBatch.Scan((t, td) => t + td, lastTranscript).ToList();
                    var transcript = lastTranscript = transcriptBatch[^1];
                    var newStableTranscript = transcriptBatch.FirstOrDefault(t => t.IsStable) ?? Transcript.Empty;
                    var prefix = stableTranscript.Text;
                    // The diff represents the changes between the current transcript (transcript) and the stable transcript (stableTranscript).
                    // This diff is distinct from the transcriptDiff object because transcriptDiff is derived from the unstable transcript, which may still be undergoing changes.
                    // The purpose of this calculation is to isolate the differences that have occurred since the last stable state of the transcript.
                    var diffSinceStable = transcript - stableTranscript;
                    var content = diffSinceStable.TextDiff.Suffix ?? "";
                    if (!ReferenceEquals(newStableTranscript, Transcript.Empty))
                        stableTranscript = newStableTranscript;
                    if (content.IsNullOrWhiteSpace())
                        continue;

                    var translatedContent = await TranslationsBackend.Translate(translationId, prefix, content, cancellationToken).ConfigureAwait(false);
                    if (OrdinalIgnoreCaseEquals(translatedContent, Constants.Translation.NoTranslationNeededText))
                        translatedContent = content; // No translation needed, use original content

                    if (!translatedContent.StartsWith(" ", StringComparison.OrdinalIgnoreCase) && stableTranslatedTranscript.Text.Length > 0)
                        translatedContent = $" {translatedContent}";

                    // DebugLog?.LogDebug("Translation in progress #{TranslationId}: {Content}->{TranslatedContent}", translationId, content, translatedContent);
                    var translatedTranscript = stableTranslatedTranscript.WithSuffix(translatedContent, diffSinceStable.TimeMapDiff.Suffix.Scale(content.Length, translatedContent.Length));
                    var translatedDiffSinceStable = translatedTranscript - stableTranslatedTranscript;
                    await writer.WriteAsync(translatedDiffSinceStable, cancellationToken).ConfigureAwait(false);
                    if (diffSinceStable.IsStable)
                        stableTranslatedTranscript = translatedTranscript with { IsStable = true };
                }
            }
            catch (Exception ex) {
                error = ex;
            }
            finally {
                try {
                    // Update the final translation
                    await FinalizeTranslation(translationId,
                            stableTranslatedTranscript.Text,
                            stableTranscript.Text,
                            cancellationToken)
                        .ConfigureAwait(false);
                    // Enqueue the translation command to retranslate full finalized transcript with larger context and model
                    var cmd = new TranslationsBackend_Translate(translationId, true);
                    await Queues.Enqueue(cmd, cancellationToken).ConfigureAwait(false);

                    // delay to ensure ITranslationsBackend.Get() will return the finalized translation
                    await Task.Delay(TranslateThrottleDelay * 2, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex2) {
                    if (error == null)
                        error = ex2; // Preserve the original error if it exists
                    else
                        Log.LogError(ex2, "Error while finalizing translation #{TranslationId}", translationId);
                }
                finally {
                    if (error != null)
                        Log.LogError(error, "Error while translating transcript #{TranslationId}", translationId);
                    writer.Complete(error);
                }
            }
        }, CancellationToken.None); // No need to cancel this task, it should finalize translation even if the caller cancels

        return reader.ReadAllAsync(cancellationToken);
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
                IsRealtime = true,
                Content = "",
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
                SourceContentHash = ChatEntryHashExt.GetContentHashString(originalText),
            }));
        return Commander.Call(cmd, cancellationToken)
            .Catch(Log, "Failed to finalize streaming translation #{TranslationId}", translationId);
    }
}
