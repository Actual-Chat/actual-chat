using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class TranscriptUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private ChatUI ChatUI => Hub.ChatUI;
    private TranslationUI TranslationUI => Hub.TranslationUI;

    [ComputeMethod]
    public virtual async Task<string> GetStreamingText(ChatEntryId id, CancellationToken cancellationToken)
        => (await GetTranscriptState(id, cancellationToken).ConfigureAwait(false)).Text;

    [ComputeMethod]
    public virtual async Task<TranscriptStreamReaderState> GetTranscriptState(
        ChatEntryId id,
        CancellationToken cancellationToken)
    {
        // Gating on IsContentStreaming before GetReader keeps a non-streaming entry - i.e. nearly
        // every entry ever scrolled past - from producing a pinned GetReader computed at all.
        if (!await IsContentStreaming(id, cancellationToken).ConfigureAwait(false))
            return TranscriptStreamReaderState.None;

        // The reader outlives its own stream: it stays until the entry stops streaming, so its last
        // state covers the gap between the transcript ending and the server clearing ContentStreamId.
        var reader = await GetReader(id, cancellationToken).ConfigureAwait(false);
        return reader is null
            ? TranscriptStreamReaderState.None
            : await reader.State.Use(cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<StreamingState?> GetStreamingState(ChatEntryId id, CancellationToken cancellationToken)
    {
        var entry = await ChatUI.GetEntry(id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return null;

        var mustTranslate = await TranslationUI
            .MustTranslate(entry, isForStreaming: true, cancellationToken)
            .ConfigureAwait(false);
        if (!mustTranslate)
            return entry.ContentStreamId.IsNullOrEmpty()
                ? null
                : new StreamingState(StreamId.Parse(entry.ContentStreamId), entry.Content, false);

        var translation = await TranslationUI.GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (translation?.StreamId is not null) {
            // Already streaming a translated transcript
            return new StreamingState(translation.StreamId, entry.Content, IsTranslation: true);
        }

        if (entry.ContentStreamId.IsNullOrEmpty())
            return null; // No source stream, so there's nothing to translate

        var sourceStreamId = StreamId.Parse(entry.ContentStreamId);
        var translationLanguage = await TranslationUI
            .GetTranslationLanguage(id.ChatId, cancellationToken)
            .ConfigureAwait(false);
        var streamId = StreamId.New(sourceStreamId, translationLanguage);
        // We can start an ad-hoc translation stream
        return new StreamingState(streamId, entry.Content, IsTranslation: true);
    }

    // Protected/internal methods

    // MinCacheDuration spans Constants.Chat.MaxEntryDuration plus finalization - see the body.
    [ComputeMethod(MinCacheDuration = 250)]
    protected virtual async Task<TranscriptStreamReader?> GetReader(
        ChatEntryId id,
        CancellationToken cancellationToken)
    {
        // This computed owns the reader: creating and starting it here is atomic w.r.t. any other
        // caller, and invalidation - the one thing that also re-drives every consumer - disposes it.
        // It's also all that roots the reader, since ComputedRegistry holds computeds weakly, hence
        // the pin above; past a transcript's own lifetime an unobserved reader means a stuck entry,
        // and letting it go is then the outcome we want.
        if (!await IsContentStreaming(id, cancellationToken).ConfigureAwait(false))
            return null;

        var reader = new TranscriptStreamReader(id, Hub);
        Computed.GetCurrent().Invalidated += _ => reader.DisposeSilently();
        reader.Start();
        return reader;
    }

    // Consolidating: GetEntry resolves through a 5-entry tile, so it invalidates whenever any
    // neighboring entry changes, while GetReader must see only the streaming on/off transitions -
    // anything else destroys and rebuilds a reader mid-stream.
    [ComputeMethod(ConsolidationDelay = 0)]
    protected virtual async Task<bool> IsContentStreaming(ChatEntryId id, CancellationToken cancellationToken)
        => await ChatUI.GetEntry(id, cancellationToken).ConfigureAwait(false) is { IsContentStreaming: true };

    // Nested types

    public sealed record StreamingState(StreamId StreamId, string Content, bool IsTranslation);
}
