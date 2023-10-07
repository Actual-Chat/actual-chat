using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class SystemProperties : DbServiceBase<UsersDbContext>, ISystemProperties
{
    private static readonly Task<string> ApiVersionTask = Task.FromResult(Constants.Api.Version);

    public SystemProperties(IServiceProvider services) : base(services) { }

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
        var (session, everywhere) = command;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            // It should happen inside this block to make sure it runs on every node
            var agentInfo = Services.GetRequiredService<AgentInfo>();
            var operation = context.Operation();
            if (everywhere || operation.AgentId == agentInfo.Id)
                ComputedRegistry.Instance.InvalidateEverything();
            return;
        }

        var accounts = Services.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);

        // We must call CreateCommandDbContext to make sure this operation is logged in the Users DB
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
    }
}
