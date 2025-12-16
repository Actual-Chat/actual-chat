using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using MemoryPack;

namespace ActualChat.Users.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class MasterFlow : Flow<Unit>, IMasterFlow
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public HashSet<string> AppliedMigrations { get; set; } = new (StringComparer.Ordinal);

    protected override ValueTask Init(CancellationToken cancellationToken)
    {
        AppliedMigrations = new (StringComparer.Ordinal);
        return default;
    }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        if (!AppliedMigrations.Contains(nameof(StartDigestFlows))) {
            await StartDigestFlows(cancellationToken).ConfigureAwait(false);
            AppliedMigrations.Add(nameof(StartDigestFlows));
        }
    }

    // Private methods

    private async Task StartDigestFlows(CancellationToken cancellationToken)
    {
        const int pageSize = 1000;
        var dbHub = Runtime.Services.DbHub<UsersDbContext>();
        var dbContext = await dbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var accountIds = dbContext.Accounts
            .OrderBy(x => x.Id)
            .ReadAsync(pageSize, x => x.Id, cancellationToken);
        await foreach (var accountId in accountIds.ConfigureAwait(false)) {
            var userId = UserId.Parse(accountId);
            await Hub.Get<DigestFlow>(userId.Id.Value, cancellationToken).ConfigureAwait(false);
        }
    }
}
