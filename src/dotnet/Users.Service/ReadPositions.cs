using System.Diagnostics.CodeAnalysis;
using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

[SuppressMessage("Usage", "MA0006:Use String.Equals instead of equality operator")]
public class ReadPositions: DbServiceBase<UsersDbContext>, IReadPositions
{
    private IAccounts Accounts { get; }
    private IReadPositionsBackend Backend { get; }

    public ReadPositions(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Backend = services.GetRequiredService<IReadPositionsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<long?> GetOwn(Session session, string chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        return await Backend.Get(account.Id, chatId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task Set(IReadPositions.SetCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, readEntryId) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return;

        await Commander.Call(
            new IReadPositionsBackend.SetCommand(account.Id, chatId, readEntryId),
            true, cancellationToken)
            .ConfigureAwait(false);
    }
}
