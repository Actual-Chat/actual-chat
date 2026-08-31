using ActualChat.Flows;
using ActualChat.Queues;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Coalesces removal/restore-triggered resummarization: resume events for one conversation dedup by
/// quantized time, so a burst of removals yields one summarizer run over the conversation's range
/// as it exists at execution time.
/// </summary>
[Flow(ResumeTimeout = 60)]
[DataContract, MessagePackObject(true)]
public sealed partial class ConversationRefreshFlow : Flow<Unit>
{
    private IConversationsBackend ConversationsBackend => field ??= Services.GetRequiredService<IConversationsBackend>();
    private ConversationId ConversationId => field ??= ConversationId.Parse(Id.Arguments);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        // Re-reading the range here rather than at enqueue time is the point: a conversation that grew
        // or was re-summarized during the delay is refreshed as it exists now, never shrunk back.
        var conversation = await ConversationsBackend.Get(ConversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
            return;

        var summarize = new ConversationBackend_Summarize(ConversationId.ChatId, [conversation.EntryLidRange]);
        await Services.Queues().Enqueue(summarize, cancellationToken).ConfigureAwait(false);
    }
}
