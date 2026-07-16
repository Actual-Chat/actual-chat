using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class TranscriptUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    // This cache is the reader's only owner, so an eviction that doesn't dispose orphans a running worker.
    private readonly IThreadSafeLruCache<ChatEntryId, TranscriptStreamReader> _previewReaders
        = new ThreadSafeLruCache<ChatEntryId, TranscriptStreamReader>(32, evictionHandler: DisposeEvictedReader);

    private ChatUI ChatUI => Hub.ChatUI;
    private TranslationUI TranslationUI => Hub.TranslationUI;

    [ComputeMethod]
    public virtual async Task<string> GetStreamingText(ChatEntryId id, CancellationToken cancellationToken)
    {
        // A streaming entry's persisted Content is empty - its text exists only in the stream.
        var entry = await ChatUI.GetEntry(id, cancellationToken).ConfigureAwait(false);
        if (entry is not { IsContentStreaming: true }) {
            // GetEntry invalidates when the entry finalizes, so this is where a finished reader is retired.
            if (_previewReaders.TryGetValue(id, out var finished)) {
                _previewReaders.Remove(id);
                _ = finished.DisposeSilentlyAsync();
            }
            return "";
        }

        var reader = _previewReaders.GetOrAdd(id, entryId => {
            var newReader = new TranscriptStreamReader(entryId, Hub);
            newReader.Start();
            return newReader;
        });
        var state = await reader.State.Use(cancellationToken).ConfigureAwait(false);
        return state.RetainedText + state.ChangedText + state.AnimatedText;
    }

    [ComputeMethod]
    public virtual async Task<StreamingState?> GetStreamingState(ChatEntryId id, CancellationToken cancellationToken)
    {
        var entry = await ChatUI.GetEntry(id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return null;

        var mustTranslate = await TranslationUI.MustTranslate(entry, isForStreaming: true, cancellationToken).ConfigureAwait(false);
        if (!mustTranslate)
            return entry.ContentStreamId.IsNullOrEmpty()
                ? null
                : new StreamingState(StreamId.Parse(entry.ContentStreamId), entry.Content, false);

        var translation = await TranslationUI.GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (translation?.StreamId is not null)
            return new StreamingState(translation.StreamId, entry.Content, IsTranslation: true); // Already streaming translated transcript.

        if (entry.ContentStreamId is not { } contentStreamId)
            return null; // No source stream. We can't start a translation stream.

        if (contentStreamId.IsNullOrEmpty())
            return null;

        var sourceStreamId = StreamId.Parse(contentStreamId);
        var translationLanguage = await TranslationUI
            .GetTranslationLanguage(id.ChatId, cancellationToken)
            .ConfigureAwait(false);
        var streamId = StreamId.New(sourceStreamId, translationLanguage);
        return new StreamingState(streamId, entry.Content, IsTranslation: true); // We can start ad-hoc translation stream.
    }

    // Private methods

    private static void DisposeEvictedReader(ChatEntryId id, TranscriptStreamReader reader)
        => _ = reader.DisposeSilentlyAsync();

    // Nested types

    public sealed record StreamingState(StreamId StreamId, string Content, bool IsTranslation);
}
