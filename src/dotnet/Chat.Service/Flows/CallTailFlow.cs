using ActualChat.Flows;
using ActualChat.Queues;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Grows a closed call's conversation over the transcripts that land after it, then re-sizes it once
/// they are final. A transcript's entry is created on its first non-empty result, so the last utterance
/// of a call routinely gets its id after the call was already materialized.
/// </summary>
// Re-materializing is the whole mechanism: ConversationBackend_Materialize recounts the messages and
// the words over the (possibly grown) range and schedules the summary refresh - which the close itself
// could not schedule, since at that moment the transcripts were still empty.
[Flow(DelayQuanta = 5, ResumeTimeout = 60)]
[DataContract, MessagePackObject(true)]
public sealed partial class CallTailFlow : Flow<Unit>
{
    private const int MaxEntries = 1000;
    private const int MaxTailEntries = 20;
    private static readonly TimeSpan Throttle = TimeSpan.FromSeconds(15);
    // Past the point where StreamingEntryFixupFlow has closed or removed whatever the pipeline
    // couldn't - its own delay plus a run interval and a scheduling quantum - so waiting any longer
    // can only be waiting for something that will never arrive.
    private static readonly TimeSpan GiveUpDelay =
        Constants.Chat.StreamingEntryFixupDelay + TimeSpan.FromMinutes(2);

    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IConversationsBackend ConversationsBackend => field ??= Services.GetRequiredService<IConversationsBackend>();
    private ConversationId ConversationId => field ??= ConversationId.Parse(Id.Arguments);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var conversation = await ConversationsBackend.Get(ConversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is not { IsCall: true })
            return; // Removed, or never a call - nothing here may touch it

        var entries = await ChatsBackend
            .ListNewEntries(
                ConversationId.ChatId,
                ConversationId.StartEntryLid - 1,
                MaxEntries + MaxTailEntries,
                cancellationToken)
            .ConfigureAwait(false);
        var endEntryLid = GetEndEntryLid(conversation, entries);
        var hasStreaming = entries.Any(e => e.LocalId <= endEntryLid && e.IsContentStreaming);
        var isSettled = !hasStreaming
            && ResumedAt >= conversation.EndsAt + Constants.Transcription.EntryFinalizationTimeout;
        var isGivingUp = ResumedAt >= conversation.EndsAt + GiveUpDelay;
        var isLastPass = isSettled || isGivingUp;
        if (endEntryLid > conversation.EndEntryLid || isLastPass) {
            // The last pass re-materializes even when the range didn't grow: that's the re-sizing, and
            // whether the first one was made over still-empty transcripts isn't knowable from here.
            var materialize = new ConversationBackend_Materialize(conversation with { EndEntryLid = endEntryLid });
            await Services.Queues().Enqueue(materialize, cancellationToken).ConfigureAwait(false);
        }

        if (isLastPass)
            return;

        Runtime.StageResumeIn(Throttle);
    }

    // Private methods

    private static long GetEndEntryLid(Conversation conversation, IReadOnlyList<ChatEntry> entries)
    {
        // An entry belongs to the call when its speech started before the call ended; a message typed
        // afterwards begins after it. Audio is not the test - a finalized entry drops its Audio when the
        // media didn't save. Entries that fail the test but sit before one that passes are swallowed:
        // a range is contiguous, so the choice is between pulling in a line typed right after the
        // hang-up and leaving the late transcript outside, which is the bug being fixed.
        var endEntryLid = conversation.EndEntryLid;
        var scannedCount = 0;
        foreach (var entry in entries) {
            if (entry.LocalId <= conversation.EndEntryLid)
                continue;
            if (++scannedCount > MaxTailEntries)
                break;
            if (entry.IsSystemEntry || entry.BeginsAt > conversation.EndsAt)
                continue;

            endEntryLid = entry.LocalId;
        }

        return endEntryLid;
    }
}
