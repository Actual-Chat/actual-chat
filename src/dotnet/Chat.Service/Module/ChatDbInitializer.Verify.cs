using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Module;

public partial class ChatDbInitializer
{
    private async Task Verify(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        Log.LogInformation("Verifying DB...");

        var chatIds = await dbContext.Chats
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var chatId in chatIds) {
            var thisChatEntries = dbContext.ChatEntries.Where(e => e.ChatId == chatId);
            var duplicateEntries = await (
                from e in thisChatEntries
                let count = thisChatEntries.Count(e1 => e1.LocalId == e.LocalId && e1.Kind == e.Kind)
                where count > 1
                select e
                ).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (duplicateEntries.Count <= 0)
                continue;

            Log.LogCritical("Duplicate entries in Chat #{ChatId}:", chatId);
            foreach (var e in duplicateEntries)
                Log.LogCritical(
                    "- Entry w/ CompositeId = {CompositeId}, Id = {Id}, Type = {Type}, '{Content}'",
                    e.Id,
                    e.LocalId,
                    e.Kind,
                    e.Content);
        }
    }
}
