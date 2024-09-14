using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using MemoryPack;

namespace ActualChat.Users.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class MasterFlow : Flow
{
    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        const int pageSize = 1000;
        var dbHub = Host.Services.DbHub<UsersDbContext>();
        var dbContext = await dbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var accountIds = dbContext.Accounts
            .OrderBy(x => x.Id)
            .ReadAsync(pageSize, x => x.Id, cancellationToken);
        await foreach (var accountId in accountIds.ConfigureAwait(false)) {
            var userId = UserId.Parse(accountId);
            await Host.Flows.GetOrStart<DigestFlow>(userId.Id, cancellationToken).ConfigureAwait(false);
        }
        return Wait(nameof(OnReset));
    }
}
