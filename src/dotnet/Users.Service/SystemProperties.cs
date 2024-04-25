using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.Internal;

namespace ActualChat.Users;

public class SystemProperties(IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), ISystemProperties
{
    private static readonly Task<string> ApiVersionTask = Task.FromResult(Constants.Api.StringVersion);

    // Not a [ComputeMethod]!
    public Task<double> GetTime(CancellationToken cancellationToken)
        => Task.FromResult(Clocks.SystemClock.Now.EpochOffset.TotalSeconds);

    // [ComputeMethod]
    public virtual Task<string> GetApiVersion(CancellationToken cancellationToken)
        => ApiVersionTask;

    // [CommandHandler]
    public virtual async Task OnInvalidateEverything(
        SystemProperties_InvalidateEverything command,
        CancellationToken cancellationToken)
    {
        // NOTE(AY): Maybe add backend & implement IApiCommand?

        var (session, everywhere) = command;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating) {
            // It should happen inside this block to make sure it runs on every node
            var hostId = Services.GetRequiredService<HostId>();
            var operation = context.Operation;
            if (everywhere || operation.HostId == hostId.Id)
                ComputedRegistry.Instance.InvalidateEverything();
            return;
        }

        var accounts = Services.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);

        // We must call CreateCommandDbContext to make sure this operation is logged in the Users DB
        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnPruneComputedGraph(
        SystemProperties_PruneComputedGraph command,
        CancellationToken cancellationToken)
    {
        // NOTE(AY): Maybe add backend & implement IApiCommand?

        var (session, everywhere) = command;
        var context = CommandContext.GetCurrent();
        var computedGraphPruner = Services.GetRequiredService<ComputedGraphPruner>();

        if (Computed.IsInvalidating) {
            // It should happen inside this block to make sure it runs on every node
            var hostId = Services.GetRequiredService<HostId>();
            var operation = context.Operation;
            if (everywhere || operation.HostId == hostId.Id)
                _ = computedGraphPruner.PruneOnce(CancellationToken.None);
            return;
        }

        var accounts = Services.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);

        // We must call CreateCommandDbContext to make sure this operation is logged in the Users DB
        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
    }
}
