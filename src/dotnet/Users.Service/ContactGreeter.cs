using ActualChat.Contacts;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class ContactGreeter(IServiceProvider services) : ActivatedWorkerBase(services)
{
    private const int BatchSize = 100;

    private DbHub<UsersDbContext> DbHub => field ??= Services.DbHub<UsersDbContext>();
    private ICommander Commander => field ??= Services.Commander();

    protected override async Task<bool> OnActivate(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbAccounts = await dbContext.Accounts.Where(x => !x.IsGreetingCompleted)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var userIds = dbAccounts.Select(x => UserId.Parse(x.Id)).ToList();
        foreach (var userId in userIds)
            await Commander.Call(new ContactsBackend_Greet(userId), cancellationToken).ConfigureAwait(false);
        return dbAccounts.Count == 0;
    }
}
