using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Live;
using ActualChat.Queues;
using ActualChat.Streaming;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Writes a closed live conversation to the saved conversations, over the exact boundaries the live
/// sessions backend recorded for it. Its summary is saved as it is, unless it stopped short of the end.
/// </summary>
// The backend lists a closed conversation until a saved one overlaps it, so the flow ends once the
// listing drops it, and gives up after MaxAttempts if the write keeps failing.
[Flow(ResumeTimeout = 60)]
[DataContract, MessagePackObject(true)]
public sealed partial class ClosedLiveConversationFlow : Flow<Unit>
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    private ILiveSessionsBackend LiveSessionsBackend => field ??= Services.GetRequiredService<ILiveSessionsBackend>();

    private ConversationId ConversationId => field ??= ConversationId.Parse(Id.Arguments);

    [DataMember(Order = 0), Key(0)]
    public int Attempts { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var chatId = ConversationId.ChatId;
        var ids = await LiveSessionsBackend.ListLiveConversations(chatId, cancellationToken).ConfigureAwait(false);
        if (!ids.Contains(ConversationId))
            return; // Saved already, or expired

        var closed = await LiveSessionsBackend.GetLiveConversation(ConversationId, cancellationToken).ConfigureAwait(false);
        if (closed is null or { IsOpen: true })
            return;

        ICommand<Conversation?> command = IsSummaryStale(closed)
            ? new ConversationBackend_Summarize(chatId, [new Range<long>(closed.MaterializedStartLid, closed.EndLid)]) {
                IsLiveMaterialization = true,
            }
            : new ConversationBackend_Materialize(closed.ToMaterializedConversation());
        await Services.Queues().Enqueue(command, cancellationToken).ConfigureAwait(false);
        if (++Attempts < MaxAttempts)
            Runtime.StageResumeIn(RetryDelay);
    }

    // Private methods

    private bool IsSummaryStale(LiveConversation conversation)
        => conversation.EndLid - conversation.SummaryEndLid >= Settings.Summarization.MinLiveConversationEntries;
}
