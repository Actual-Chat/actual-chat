using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

public class ChatUsagesBackend(IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), IChatUsagesBackend
{
    private const int RecencyListLimit = 100;

    public virtual async Task<ApiArray<ChatId>> GetRecencyList(UserId userId, ChatUsageListKind kind, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var chatUsages = GetRecencyList(dbContext, userId, kind);
        var chatSids = await chatUsages
            .Take(RecencyListLimit + 1)
            .Select(c => c.ChatId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (chatSids.Count > RecencyListLimit)
            _ = Commander.Call(new ChatUsagesBackend_PurgeRecencyList(userId, kind, RecencyListLimit), default);

        var chatIds = chatSids.Select(c => new ChatId(c)).ToApiArray();
        return chatIds;
    }

    public virtual async Task OnRegisterUsage(
        ChatUsagesBackend_RegisterUsage command,
        CancellationToken cancellationToken)
    {
        var (userId, kind, chatId, accessTimeOpt) = command;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            if (context.Operation.Items.GetOrDefault<bool>())
                _ = GetRecencyList(userId, kind, default);
            return;
        }

        var accessTime = accessTimeOpt ?? Clocks.SystemClock.Now;
        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var id = DbChatUsage.ComposeId(userId, kind, chatId);
        var dbChatRecency = await dbContext.ChatUsages.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        bool hasChanges = false;
        if (dbChatRecency == null) {
            dbChatRecency = new DbChatUsage {
                Id = id,
                UserId = userId,
                Kind = kind,
                ChatId = chatId,
                AccessedAt = accessTime
            };
            dbContext.Add(dbChatRecency);
            hasChanges = true;
        }
        else if (dbChatRecency.AccessedAt < accessTime) {
            dbChatRecency.AccessedAt = accessTime;
            hasChanges = true;
        }

        if (hasChanges)
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.Set(hasChanges);
    }

    public virtual async Task OnPurgeRecencyList(
        ChatUsagesBackend_PurgeRecencyList command,
        CancellationToken cancellationToken)
    {
        var (userId, kind, size) = command;

        if (Invalidation.IsActive) {
            _ = GetRecencyList(userId, kind, default);
            return;
        }

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var chatUsages = GetRecencyList(dbContext, userId, kind);
        var chatUsagesToRemove = await chatUsages
            .Skip(size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.RemoveRange(chatUsagesToRemove);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (entry, author, changeKind, _) = eventCommand;
        if (entry.IsSystemEntry)
            return;

        if (changeKind != ChangeKind.Create)
            return;

        var chatId = entry.ChatId;
        if (!chatId.IsPeerChat(out _))
            return;

        var userId = author.UserId;
        var command = new ChatUsagesBackend_RegisterUsage(userId,
            ChatUsageListKind.PeerChatsWroteTo,
            chatId,
            entry.BeginsAt.ToDateTime());
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    // Internals and private

    private static IOrderedQueryable<DbChatUsage> GetRecencyList(UsersDbContext dbContext, UserId userId, ChatUsageListKind kind)
    {
        var idPrefix = DbChatUsage.ComposeIdPrefix(userId, kind);
        return dbContext.ChatUsages
            .Where(x => x.Id.StartsWith(idPrefix))
            .OrderByDescending(x => x.AccessedAt)
            .ThenBy(x => x.Id);
    }
}
