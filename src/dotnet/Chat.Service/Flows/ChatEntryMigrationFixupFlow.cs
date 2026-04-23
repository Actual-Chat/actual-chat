using ActualChat.Chat.Db;
using ActualChat.Flows;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Fixes up entries that <see cref="ChatEntryMigrationFlow"/> missed.
/// - Audio entry missing or has no content: skips (leaves as-is).
/// - Audio entry valid: retries media creation via base flow.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ChatEntryMigrationFixupFlow : ChatEntryMigrationFlow
{
    protected override async Task ProcessOne(ChatDbContext dbContext, DbChatEntry textEntry, CancellationToken cancellationToken)
    {
        var chatId = ChatId.Parse(textEntry.ChatId);
        var audioEntryLid = textEntry.AudioEntryId!.Value;

        var audioEntryDbId = $"{chatId.Value}:1:{audioEntryLid.Format()}";
        var audioEntry = await dbContext.ChatEntries
            .FirstOrDefaultAsync(e => e.Id == audioEntryDbId, cancellationToken)
            .ConfigureAwait(false);

        if (audioEntry == null) {
            Console.LogWarning($"Skipping {textEntry.Id}: audio entry {audioEntryDbId} missing");
            return;
        }

        if (audioEntry.Content.IsNullOrEmpty()) {
            Console.LogWarning($"Skipping {textEntry.Id}: audio entry {audioEntryDbId} has no content");
            return;
        }

        // Audio entry is valid — retry media creation
        await base.ProcessOne(dbContext, textEntry, cancellationToken).ConfigureAwait(false);
    }
}
