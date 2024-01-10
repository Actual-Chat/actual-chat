namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatEditorUI
{
    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(HideWhenRelatedEntryRemoved)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 3), Log)
            .RunIsolated(StopToken);

    private async Task HideWhenRelatedEntryRemoved(CancellationToken cancellationToken)
    {
        var cRelatedChatEntry = await Computed
            .Capture(() => ComputeRelatedChatEntry(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var change in cRelatedChatEntry.Changes(cancellationToken).ConfigureAwait(false))
        {
            var (chatEntryLink, chatEntry) = change.Value;
            if (chatEntryLink != null && chatEntry == null)
                await HideRelatedEntry().ConfigureAwait(false);
        }
    }

    [ComputeMethod]
    protected virtual async Task<(RelatedChatEntry?, ChatEntry?)> ComputeRelatedChatEntry(CancellationToken cancellationToken)
    {
        var entryLink = await RelatedChatEntry.Use(cancellationToken).ConfigureAwait(false);
        if (entryLink == null)
            return (null, null);

        var entry = await Chats.GetEntry(Session, entryLink.Value.Id, cancellationToken).ConfigureAwait(false);
        return (entryLink, entry);
    }
}
