using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class ChatThreadsBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), IChatThreadsBackend
{
    public virtual async Task<ApiArray<ChatId>> ListIds(ChatId parentChatId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var sChatThreadIds = await dbContext.ChatThreads
            .Where(c => c.ParentChatId == parentChatId.Value)
            .OrderByDescending(c => c.ThreadId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return sChatThreadIds.Select(ChatId.ParseOrNone).Where(c => !c.IsNone).ToApiArray();
    }

    public virtual async Task<ChatThread> OnStart(ChatThreadsBackend_Start command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive) {
            _ = ListIds(command.ParentChatId, default);
            return default!;
        }

        var (parentChatId, title) = command;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.ChatThreads.Lock(parentChatId, cancellationToken).ConfigureAwait(false);
        var maxThreadId = await dbContext.ChatThreads
            .Where(c => c.ParentChatId == parentChatId.Value)
            .MaxAsync(c => (ulong?)c.ThreadId, cancellationToken)
            .ConfigureAwait(false);
        var threadId = (maxThreadId ?? 0) + 1;
        if (title.IsNullOrEmpty())
            title = $"Thread #{threadId}";
        var chatId = parentChatId.CreateThreadId(threadId);
        var chatThread = new ChatThread(chatId) {
            Version = VersionGenerator.NextVersion(),
            CreatedAt = Clocks.SystemClock.Now,
            Title = title,
        };
        var dbChatThread = new DbChatThread(chatThread);
        dbContext.ChatThreads.Add(dbChatThread);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbChatThread.ToModel();
    }
}
