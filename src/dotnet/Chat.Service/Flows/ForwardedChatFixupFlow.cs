using ActualChat.Chat.Db;
using ActualChat.Flows;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Repoints forwarded entries whose source chat is gone at its published copy, like ChatEntryMessageView does
/// on the fly via <see cref="IChatsBackend.GetForwardChatReplacement"/>, and takes the author from the copied entry.
/// It covers the chats removed by the time it runs; a published copy whose source still exists is left alone.
/// Rows are edited directly, so nothing is invalidated.
/// The scan goes chat by chat, then by local id: (LastChatId, LastLocalId) is the cursor.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MessagePackObject(true)]
public sealed partial class ForwardedChatFixupFlow : Flow<(Moment, long)>
{
    private const int BatchSize = 200;
    private const int MaxChatsPerBatch = 200;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(1).ToRandom(0.25);

    private DbHub<ChatDbContext> DbHub => field ??= Services.DbHub<ChatDbContext>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();

    [DataMember(Order = 0), Key(0)]
    public string? LastChatId { get; set; }
    [DataMember(Order = 1), Key(1)]
    public long LastLocalId { get; set; }
    [DataMember(Order = 2), Key(2)]
    public long FixedCount { get; set; }
    [DataMember(Order = 3), Key(3)]
    public long ScannedCount { get; set; }
    [DataMember(Order = 4), Key(4)]
    public long ChatCount { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var replacements = new Dictionary<ChatId, ChatId?>();
        var entriesLeft = BatchSize;
        var chatsLeft = MaxChatsPerBatch;
        var isDone = false;
        while (entriesLeft > 0 && chatsLeft > 0) {
            if (LastChatId is null && !await MoveToNextChat(dbContext, cancellationToken).ConfigureAwait(false)) {
                isDone = true;
                break;
            }

            var chatId = LastChatId;
            var lastLocalId = LastLocalId;
            var batch = await dbContext.ChatEntries
                .Where(e => e.ChatId == chatId && e.LocalId > lastLocalId && e.ForwardedChatEntryId != null)
                .OrderBy(e => e.LocalId)
                .Take(entriesLeft)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var entry in batch) {
                if (await Fix(dbContext, entry, replacements, cancellationToken).ConfigureAwait(false))
                    FixedCount++;
            }
            ScannedCount += batch.Count;
            entriesLeft -= batch.Count;

            if (batch.Count > 0)
                LastLocalId = batch[^1].LocalId;
            if (entriesLeft > 0) {
                // The chat is exhausted
                chatsLeft--;
                ChatCount++;
                if (!await MoveToNextChat(dbContext, cancellationToken).ConfigureAwait(false)) {
                    isDone = true;
                    break;
                }
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (isDone) {
            Console.Log($"Completed: fixed {FixedCount} of {ScannedCount} forwarded entries in {ChatCount} chats");
            SetResult((Hub.Clocks.SystemClock.Now, FixedCount));
            return;
        }

        Console.Log($"Progress: fixed {FixedCount} of {ScannedCount} forwarded entries in {ChatCount} chats");
        Runtime.StageResumeIn(BatchDelay.Next());
    }

    // Private methods

    private async Task<bool> MoveToNextChat(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var lastChatId = LastChatId ?? "";
        var nextChatId = await dbContext.Chats
            .Where(c => string.Compare(c.Id, lastChatId) > 0)
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (nextChatId is null)
            return false;

        LastChatId = nextChatId;
        LastLocalId = 0;
        return true;
    }

    private async Task<bool> Fix(
        ChatDbContext dbContext,
        DbChatEntry entry,
        Dictionary<ChatId, ChatId?> replacements,
        CancellationToken cancellationToken)
    {
        if (!ChatEntryId.TryParse(entry.ForwardedChatEntryId, out var sourceEntryId))
            return false;

        var sourceChatId = sourceEntryId.ChatId;
        if (!replacements.TryGetValue(sourceChatId, out var replacementId)) {
            replacementId = await FindReplacement(sourceChatId, cancellationToken).ConfigureAwait(false);
            replacements[sourceChatId] = replacementId;
        }
        if (replacementId is null)
            return false;

        var replacementEntryId = ChatEntryId.New(replacementId, sourceEntryId.LocalId);
        var replacementAuthorId = await dbContext.ChatEntries
            .Where(e => e.Id == replacementEntryId.Value)
            .Select(e => e.AuthorId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        entry.ForwardedChatEntryId = replacementEntryId.Value;
        if (!replacementAuthorId.IsNullOrEmpty())
            entry.ForwardedAuthorId = replacementAuthorId;
        entry.Version = DbHub.VersionGenerator.NextVersion(entry.Version);
        return true;
    }

    private async Task<ChatId?> FindReplacement(ChatId sourceChatId, CancellationToken cancellationToken)
    {
        var sourceChat = await ChatsBackend.Get(sourceChatId, cancellationToken).ConfigureAwait(false);
        if (sourceChat is not null)
            return null;

        return await ChatsBackend.GetForwardChatReplacement(sourceChatId, cancellationToken).ConfigureAwait(false);
    }
}
