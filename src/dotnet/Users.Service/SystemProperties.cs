using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.Internal;

namespace ActualChat.Users;

public class SystemProperties(IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), ISystemProperties
{
    // Not a [ComputeMethod]!
    public Task<double> GetTime(CancellationToken cancellationToken)
        => Task.FromResult(Clocks.SystemClock.Now.EpochOffset.TotalSeconds);

    // [ComputeMethod]
    public virtual Task<ServerApiInfo> GetServerApiInfo(
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        if (expectedVersion.IsNullOrEmpty())
            return Task.FromResult(new ServerApiInfo(CompatibilityLevel.Unknown));

        expectedVersion = expectedVersion.TrimStart('v');
        var clientVersionParts = expectedVersion.Split(' ');
        expectedVersion = clientVersionParts[0];
        if (!Version.TryParse(expectedVersion, out var parsedExpectedVersion))
             throw new ArgumentOutOfRangeException(nameof(expectedVersion));

        var apiVersion = ApiConstants.Version;
        var compatibilityLevel =
            apiVersion.Major == parsedExpectedVersion.Major
            ? apiVersion == parsedExpectedVersion
                ? CompatibilityLevel.Full
                : CompatibilityLevel.Compatible
            : CompatibilityLevel.Incompatible;
        return Task.FromResult(new ServerApiInfo(compatibilityLevel));
    }

    // [CommandHandler]
    public virtual async Task OnInvalidateEverything(
        SystemProperties_InvalidateEverything command,
        CancellationToken cancellationToken)
    {
        // NOTE(AY): Maybe add backend & implement IApiCommand?

        var (session, everywhere) = command;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
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

        // We must call CreateOperationDbContext to make sure this operation is logged in the Users DB
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
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

        if (Invalidation.IsActive) {
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

        // We must call CreateOperationDbContext to make sure this operation is logged in the Users DB
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
    }

    // Legacy methods - to be removed in the future

#pragma warning disable CS0618 // Type or member is obsolete

    // [ComputeMethod]
    [Obsolete("2025.06: Retired in favour of GetServerApiInfo.")]
    public virtual async Task<string> GetApiVersion(CancellationToken cancellationToken)
    {
        var serverApiInfo = await GetServerApiInfo("", cancellationToken).ConfigureAwait(false);
        return serverApiInfo.VersionString;
    }

    // [ComputeMethod]
    [Obsolete("2025.06: Retired in favour of GetServerApiInfo.")]
    public virtual async Task<SystemProperties_LegacyClientCompatibility> CheckClientCompatibility(string clientVersion, CancellationToken cancellationToken)
    {
        var serverApiInfo = await GetServerApiInfo("", cancellationToken).ConfigureAwait(false);
        return serverApiInfo.CompatibilityLevel switch {
            CompatibilityLevel.Compatible => SystemProperties_LegacyClientCompatibility.UpgradeAvailable,
            CompatibilityLevel.Incompatible => SystemProperties_LegacyClientCompatibility.Incompatible,
            _ => SystemProperties_LegacyClientCompatibility.Latest,
        };
    }

#pragma warning restore CS0618 // Type or member is obsolete
}
