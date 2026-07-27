using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

/// <summary>
/// Frontend service for managing user's read positions in chats.
/// </summary>
[SuppressMessage("Usage", "MA0006:Use String.Equals instead of equality operator")]
public class ChatPositions(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IChatPositions
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IChatPositionsBackend Backend { get; } = services.GetRequiredService<IChatPositionsBackend>();

    // [ComputeMethod]
    public virtual async Task<ChatPosition> GetOwn(
        Session session, ChatId chatId, ChatPositionKind kind,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Backend.Get(account.Id, chatId, kind, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnSet(ChatPositions_Set command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, kind, position) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return;

        var backendCommand = new ChatPositionsBackend_Set(account.Id, chatId, kind, position);
        await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
