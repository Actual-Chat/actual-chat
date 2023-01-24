using System.Diagnostics.CodeAnalysis;
using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

[SuppressMessage("Usage", "MA0006:Use String.Equals instead of equality operator")]
public class ChatPositions: DbServiceBase<UsersDbContext>, IChatPositions
{
    private IAccounts Accounts { get; }
    private IReadPositionsBackend Backend { get; }

    public ChatPositions(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Backend = services.GetRequiredService<IReadPositionsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<ChatPosition> GetOwn(
        Session session, ChatId chatId, ChatPositionKind kind,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Backend.Get(account.Id, chatId, kind, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task Set(IChatPositions.SetCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, kind, position) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var backendCommand = new IReadPositionsBackend.SetCommand(account.Id, chatId, kind, position);
        await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
