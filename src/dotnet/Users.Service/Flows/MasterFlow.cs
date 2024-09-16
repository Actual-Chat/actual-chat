using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using MemoryPack;

namespace ActualChat.Users.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class MasterFlow : Flow
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int FlowSetVersion { get; private set; }

    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        while (true) {
            var nextFlowSetVersion = FlowSetVersion + 1;
            var migrationFunc = FlowSteps.Get(GetType(), $"MigrateToVersion{nextFlowSetVersion}");
            if (migrationFunc == null)
                break;

            await migrationFunc.Invoke(this, cancellationToken).ConfigureAwait(false);
            FlowSetVersion = nextFlowSetVersion;
            return StoreAndResume(nameof(OnReset));
        }
        return WaitForEvent(FlowSteps.OnReset, InfiniteHardResumeAt);
    }

    private async Task MigrateToVersion1(CancellationToken cancellationToken)
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
    }
}
