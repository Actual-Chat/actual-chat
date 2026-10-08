using ActualChat.Chat.Module;
using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

// The only purge of a chat: it purges the history wiped up to the boundary it was given and the more
// recent ranges wiped after it, the entries retention expires, the entries of removed authors,
// and the whole chat once it's marked for removal.
// DelayQuanta coalesces the delayed resumes a burst of message removals schedules into
// one wake-up per 5-minute slot; immediate resumes (posts, removal) skip it.

[Flow(DelayQuanta = 300, ResumeTimeout = 60)]
[DataContract, MessagePackObject(true)]
public sealed partial class ChatPurgeFlow : Flow<Unit>, IInboxProcessingFlow
{
    private const int MaxAuthorsPerResume = 10;

    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private ICommander Commander => field ??= Services.Commander();

    [DataMember(Order = 0)]
    public long ClearUntilEntryLid { get; set; }
    [DataMember(Order = 1)]
    public ApiArray<AuthorId> RemovedAuthorIds { get; set; }
    // Wiped ranges past ClearUntilEntryLid, ordered and disjoint; each one goes once it's purged
    [DataMember(Order = 2)]
    public ApiArray<Range<long>> WipeEntryLidRanges { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var chatId = ChatId.Parse(Id.Arguments);
        ProcessInbox();
        var hasMoreAuthorEntries = await PurgeRemovedAuthorEntries(chatId, cancellationToken).ConfigureAwait(false);
        var wipeEntryLidRange = WipeEntryLidRanges.FirstOrDefault();
        var purgeEntryBatchCmd = new ChatsBackend_PurgeEntryBatch(chatId, ClearUntilEntryLid, wipeEntryLidRange);
        var count = await Commander.Call(purgeEntryBatchCmd, cancellationToken).ConfigureAwait(false);
        if (!wipeEntryLidRange.IsEmpty && count < Settings.CleanupBatchSize) {
            // An entry still streaming keeps its range until the stream ends
            var isWiped = count < 0
                || !await ChatsBackend
                    .HasUnpurgedEntries(chatId, wipeEntryLidRange, cancellationToken)
                    .ConfigureAwait(false);
            if (isWiped) {
                WipeEntryLidRanges = WipeEntryLidRanges.Skip(1).ToApiArray();
                if (!WipeEntryLidRanges.IsEmpty)
                    count = Settings.CleanupBatchSize;
            }
            else
                count = Math.Max(count, 0);
        }
        if (hasMoreAuthorEntries || count >= Settings.CleanupBatchSize) {
            Runtime.StageResume();
            return;
        }

        // A negative count means nothing is left to reclaim and nothing will expire on its own,
        // so the flow goes dormant: every source of new work posts or schedules its own resume.
        if (count >= 0)
            Runtime.StageResumeIn(Settings.CleanupInterval);
    }

    // Private methods

    private void ProcessInbox()
    {
        foreach (var message in Inbox.Messages.ToList()) {
            switch (message.Payload) {
            case WipeHistory wipeHistory:
                AddWipeEntryLidRange(wipeHistory.EntryLidRange);
                break;
            case RemoveAuthorEntries removeAuthorEntries:
                if (!RemovedAuthorIds.Contains(removeAuthorEntries.AuthorId))
                    RemovedAuthorIds = [..RemovedAuthorIds, removeAuthorEntries.AuthorId];
                break;
            default:
                Console.Log($"[!] Skipped an inbox message it can't handle: {message}");
                break;
            }
            Inbox.Remove(message);
        }
    }

    private void AddWipeEntryLidRange(Range<long> entryLidRange)
    {
        if (entryLidRange.IsEmptyOrNegative)
            return;

        var ranges = WipeEntryLidRanges
            .Append(entryLidRange)
            .OrderBy(r => r.Start)
            .MergeAdjacentRanges()
            .ToList();
        // The boundary swallows every range it reaches - only the gaps it leaves stay ranges
        while (ranges.Count > 0 && ranges[0].Start <= ClearUntilEntryLid + 1) {
            ClearUntilEntryLid = Math.Max(ClearUntilEntryLid, ranges[0].End - 1);
            ranges.RemoveAt(0);
        }
        WipeEntryLidRanges = ranges.ToApiArray();
    }

    private async Task<bool> PurgeRemovedAuthorEntries(ChatId chatId, CancellationToken cancellationToken)
    {
        // A batch per author per resume, so one with a long history doesn't hold the others up,
        // and a bounded number of authors per resume, so it stays within ResumeTimeout
        var remainingAuthorIds = RemovedAuthorIds.Skip(MaxAuthorsPerResume).ToList();
        var hasMore = remainingAuthorIds.Count > 0;
        foreach (var authorId in RemovedAuthorIds.Take(MaxAuthorsPerResume)) {
            var purgeCmd = new ChatsBackend_PurgeAuthorEntries(chatId, authorId);
            var count = await Commander.Call(purgeCmd, cancellationToken).ConfigureAwait(false);
            // A negative count means an import holds the chat: the author waits for the periodic resume
            if (count < 0) {
                remainingAuthorIds.Add(authorId);
                continue;
            }
            if (count >= Settings.CleanupBatchSize) {
                remainingAuthorIds.Add(authorId);
                hasMore = true;
                continue;
            }

            // The author outlives its entries - it's how they are found - so it goes once they're gone.
            // Threads and the chats of a Place have no authors of their own, only mapped ones.
            if (authorId.ChatId == chatId && !chatId.IsThread()) {
                var removeAuthorCmd = new AuthorsBackend_Remove(null, authorId, null);
                await Commander.Call(removeAuthorCmd, cancellationToken).ConfigureAwait(false);
            }
        }
        RemovedAuthorIds = remainingAuthorIds.ToApiArray();
        return hasMore;
    }

    // Nested types

    [DataContract, MessagePackObject]
    public sealed partial record WipeHistory(
        [property: DataMember(Order = 0), Key(0)] Range<long> EntryLidRange);

    [DataContract, MessagePackObject]
    public sealed partial record RemoveAuthorEntries(
        [property: DataMember(Order = 0), Key(0)] AuthorId AuthorId);
}
