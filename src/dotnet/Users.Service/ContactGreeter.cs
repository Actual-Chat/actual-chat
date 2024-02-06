using ActualChat.Contacts;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class ContactGreeter(IServiceProvider services) : ActivatedWorkerBase(services)
{
    private const int BatchSize = 100;

    private DbHub<UsersDbContext>? _dbHub;
    private ICommander? _commander;

    private DbHub<UsersDbContext> DbHub => _dbHub ??= Services.DbHub<UsersDbContext>();
    private ICommander Commander => _commander ??= Services.Commander();

    protected override async Task<bool> OnActivate(CancellationToken cancellationToken)
    {
        var dbContext = DbHub.CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var dbAccounts = await dbContext.Accounts.Where(x => !x.IsGreetingCompleted)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var userId in dbAccounts.Select(dbAccount => new UserId(dbAccount.Id)))
            await Commander.Call(new ContactsBackend_Greet(userId), cancellationToken).ConfigureAwait(false);
        return dbAccounts.Count == 0;
    }
}
