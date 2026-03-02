using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class TranscriptUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private ChatUI ChatUI => Hub.ChatUI;
    private TranslationUI TranslationUI => Hub.TranslationUI;

    [ComputeMethod]
    public virtual async Task<StreamingState?> GetStreamingState(TextEntryId id, CancellationToken cancellationToken)
    {
        var entry = await ChatUI.GetEntry(id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return null;

        var mustTranslate = await TranslationUI.MustTranslate(entry, true, cancellationToken).ConfigureAwait(false);
        if (!mustTranslate)
            return entry.StreamId.IsNullOrEmpty()
                ? null
                : new StreamingState(StreamId.Parse(entry.StreamId), entry.Content, false);

        var translation = await TranslationUI.GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (translation?.StreamId is not null)
            return new StreamingState(translation.StreamId, entry.Content, true); // Already streaming translated transcript.

        if (entry.StreamId is not {} entryStreamId)
            return null; // No source stream. We can't start a translation stream.

        if (entryStreamId.IsNullOrEmpty())
            return null;

        var sourceStreamId = StreamId.Parse(entryStreamId);
        var language = await TranslationUI.GetTargetLanguage(id.ChatId, cancellationToken).ConfigureAwait(false);
        var streamId = StreamId.New(sourceStreamId, language ?? Languages.English);
        return new StreamingState(streamId, entry.Content, true); // We can start ad-hoc translation stream.
    }

    public sealed record StreamingState(StreamId StreamId, string Content, bool IsTranslation);
}
