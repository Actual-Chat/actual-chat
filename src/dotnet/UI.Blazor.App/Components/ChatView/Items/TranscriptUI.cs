using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class TranscriptUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private ChatUI ChatUI => Hub.ChatUI;
    private TranslationUI TranslationUI => Hub.TranslationUI;

    [ComputeMethod]
    public virtual async Task<InputModel?> GetStreamingInput(ChatEntryId id, CancellationToken cancellationToken) {
        var entry = await ChatUI.GetEntry(id, cancellationToken).ConfigureAwait(false);
        if (entry == null)
            return null;

        var isTranslationStreaming = await TranslationUI.IsStreaming(entry, cancellationToken).ConfigureAwait(false);
        return new (entry.IsStreaming || isTranslationStreaming, isTranslationStreaming, entry);
    }

    public sealed record InputModel(bool IsStreaming, bool IsTranslation, ChatEntry Entry)
    {
        public bool MustStart(InputModel? old)
        {
            if (IsStreaming && old?.IsStreaming != true)
                return true;

            if (IsTranslation && old?.IsTranslation != true)
                return true;

            return false;
        }
    }
}
