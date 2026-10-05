using ActualChat.Users.Db;
using ActualLab.CommandR.Operations;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class SystemPropertiesBackend(IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), ISystemPropertiesBackend
{
    public virtual async Task OnInvalidateEverything(
        SystemPropertiesBackend_InvalidateEverything command, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = dbContext.ConfigureAwait(false);
        CommandContext.GetCurrent().Operation.StoreMode = OperationStoreMode.Operation;
    }

    public virtual async Task OnPruneComputedGraph(
        SystemPropertiesBackend_PruneComputedGraph command, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = dbContext.ConfigureAwait(false);
        CommandContext.GetCurrent().Operation.StoreMode = OperationStoreMode.Operation;
    }
}
