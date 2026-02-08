using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class MasterFlow : Flow<Unit>, IMasterFlow
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public HashSet<string> AppliedMigrations { get; set; } = new (StringComparer.Ordinal);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        await ApplyMigration(StartAccountTouchFlow).ConfigureAwait(false);
        await ApplyMigration(StartDigestFlows).ConfigureAwait(false);
    }

    // Private methods

    private async ValueTask ApplyMigration(
        Func<CancellationToken, Task> migration,
        [CallerArgumentExpression(nameof(migration))] string name = "")
    {
        if (AppliedMigrations.Contains(name))
            return;

        await migration.Invoke(Runtime.CancellationToken).ConfigureAwait(false);
        AppliedMigrations.Add(name);
    }

    // Migrations

    private async Task StartDigestFlows(CancellationToken cancellationToken)
    {
        const int pageSize = 1000;
        var dbHub = Services.DbHub<UsersDbContext>();
        var dbContext = await dbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var accountIds = dbContext.Accounts
            .OrderBy(x => x.Id)
            .ReadAsync(pageSize, x => x.Id, cancellationToken);
        await foreach (var accountId in accountIds.ConfigureAwait(false)) {
            var userId = UserId.Parse(accountId);
            await Hub.NewResumeEvent<DigestFlow>(userId.Id.Value)
                .Schedule(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private Task StartAccountTouchFlow(CancellationToken cancellationToken)
        => Hub.NewResumeEvent<AccountTouchFlow>().Schedule(cancellationToken);
}
