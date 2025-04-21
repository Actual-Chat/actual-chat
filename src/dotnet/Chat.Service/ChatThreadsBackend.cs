using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class ChatThreadsBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), IChatThreadsBackend
{
    [field: AllowNull, MaybeNull]
    private DiffEngine DiffEngine => field ??= Services.GetRequiredService<DiffEngine>();

    // [ComputeMethod]
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

    // [CommandHandler]
    public virtual async Task<ChatThread> OnChange(
        ChatThreadsBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (chatId, expectedVersion, change) = command;
        if (Invalidation.IsActive) {
            if (change.Kind is ChangeKind.Create or ChangeKind.Remove)
                _ = ListIds(command.ChatId.GetThreadParent(), default);
            return default!;
        }

        change.RequireValid();
        chatId.Require("Command.ChatId");
        chatId.IsThread.Require("Command.ChatId.IsThread");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        await dbContext.ChatThreads.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var dbChatThread = await dbContext.ChatThreads
                .FirstOrDefaultAsync(c => c.Id == chatId, cancellationToken)
                .ConfigureAwait(false);
        var oldChat = dbChatThread?.ToModel();
        ChatThread chatThread;
        if (change.IsCreate(out var update)) {
            oldChat.RequireNull();
            chatThread = new ChatThread(chatId) {
                CreatedAt = Clocks.SystemClock.Now,
            };
            chatThread = ApplyDiff(chatThread, update);
            dbChatThread = new DbChatThread(chatThread);
            dbContext.ChatThreads.Add(dbChatThread);
        }
        else if (change.IsUpdate(out update)) {
            dbChatThread.RequireVersion(expectedVersion);

            chatThread = ApplyDiff(dbChatThread.ToModel(), update);
            dbChatThread.UpdateFrom(chatThread);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (change.IsRemove()) {
            dbChatThread.Require();
            dbChatThread.RequireVersion(expectedVersion);

            dbContext.ChatThreads.Remove(dbChatThread);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        chatThread = dbChatThread.Require().ToModel();
        return chatThread;

        ChatThread ApplyDiff(ChatThread originalChatThread, ChatThreadDiff? diff) {
            var newChatThread = DiffEngine.Patch(originalChatThread, diff) with {
                Version = VersionGenerator.NextVersion(originalChatThread.Version),
            };
            return newChatThread;
        }
    }

    // [EventHandler]
    public virtual async Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var chat = eventCommand.Chat;
        if (!chat.Id.IsThread)
            return;

        var kind = eventCommand.ChangeKind;
        if (kind is ChangeKind.Update) {
            var oldChat = eventCommand.OldChat;
            if (oldChat is null || !OrdinalEquals(chat.Title, oldChat.Title))
                return;
        }

        var change = kind switch {
            ChangeKind.Create => Change.Create(new ChatThreadDiff {
                Title = chat.Title,
            }),
            ChangeKind.Update => Change.Update(new ChatThreadDiff {
                Title = chat.Title,
            }),
            ChangeKind.Remove => Change.Remove<ChatThreadDiff>(),
            _ => throw new ArgumentOutOfRangeException(),
        };
        var command = new ChatThreadsBackend_Change(chat.Id, null, change);
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }
}
