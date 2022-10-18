using System.Diagnostics.CodeAnalysis;
using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

[SuppressMessage("Usage", "MA0006:Use String.Equals instead of equality operator")]
public class ChatReadPositions: DbServiceBase<UsersDbContext>, IChatReadPositions
{
    private IAccounts Accounts { get; }
    private IChatReadPositionsBackend Backend { get; }

    public ChatReadPositions(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Backend = services.GetRequiredService<IChatReadPositionsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<long?> Get(Session session, string chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.Get(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        return await Backend.Get(account.Id, chatId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task Set(IChatReadPositions.SetReadPositionCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, readEntryId) = command;
        var account = await Accounts.Get(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return;

        await Commander.Call(
            new IChatReadPositionsBackend.SetCommand(account.Id, chatId, readEntryId),
            true, cancellationToken)
            .ConfigureAwait(false);
    }
}
