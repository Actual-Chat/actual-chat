using ActualChat.Hosting;
using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class SystemProperties : DbServiceBase<UsersDbContext>, ISystemProperties
{
    private const string MinMauiClientVersion = "0.121";
    private const string MinWasmClientVersion = "0.121";
    private static readonly Task<string?> NullStringTask = Task.FromResult((string?)null);

    private static readonly Dictionary<AppKind, string> MinClientVersions = new() {
        { AppKind.MauiApp, MinMauiClientVersion },
        { AppKind.WasmApp, MinWasmClientVersion },
    };
    private static readonly Dictionary<AppKind, Task<string?>> MinClientVersionTasks =
        MinClientVersions.ToDictionary(kv => kv.Key, kv => Task.FromResult(kv.Value))!;

    public SystemProperties(IServiceProvider services) : base(services) { }

    // Not a [ComputeMethod]!
    public Task<double> GetTime(CancellationToken cancellationToken)
        => Task.FromResult(Clocks.SystemClock.Now.EpochOffset.TotalSeconds);

    // Not a [ComputeMethod]!
    public Task<string?> GetMinClientVersion(AppKind appKind, CancellationToken cancellationToken)
        => MinClientVersionTasks.GetValueOrDefault(appKind)
            ?? NullStringTask;

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
