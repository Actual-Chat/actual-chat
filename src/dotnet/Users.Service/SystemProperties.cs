using System.Security.Cryptography;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.Internal;

namespace ActualChat.Users;

public class SystemProperties(IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), ISystemProperties
{
    private const int MinProbePayloadSize = 1024;
    private const int MaxProbePayloadSize = 256 * 1024;
    private static readonly Version MinCompatibleVersion = new(2, 15);
    private static readonly Version MinReportableClientVersion = MinCompatibleVersion;

    // Not a [ComputeMethod]!
    public Task<double> GetTime(CancellationToken cancellationToken)
        => Task.FromResult(Clocks.SystemClock.Now.EpochOffset.TotalSeconds);

    // Not a [ComputeMethod]!
    public Task<byte[]> GetProbePayload(int size, CancellationToken cancellationToken)
    {
        // Random rather than zeroed: WebSocket deflate would shrink a compressible payload
        // to nothing, so it would cross a throttled link just fine and prove nothing.
        size = size.Clamp(MinProbePayloadSize, MaxProbePayloadSize);
        return Task.FromResult(RandomNumberGenerator.GetBytes(size));
    }

    // [ComputeMethod]
    public virtual Task<ServerApiInfo> GetServerApiInfo(string expectedVersion, CancellationToken cancellationToken)
        => GetServerApiInfoNC(expectedVersion, cancellationToken);

    // Not a [ComputeMethod]!
    public Task<ServerApiInfo> GetServerApiInfoNC(string expectedVersion, CancellationToken cancellationToken)
    {
        if (expectedVersion.IsNullOrEmpty())
            return Task.FromResult(new ServerApiInfo(CompatibilityLevel.Unknown));

        expectedVersion = expectedVersion.TrimStart('v');
        var clientVersionParts = expectedVersion.Split(' ');
        expectedVersion = clientVersionParts[0];
        if (!Version.TryParse(expectedVersion, out var parsedExpectedVersion))
            return Task.FromResult(new ServerApiInfo(CompatibilityLevel.Unknown));

        var apiVersion = ApiConstants.Version;
        var compatibilityLevel = parsedExpectedVersion < MinCompatibleVersion
            ? CompatibilityLevel.Incompatible
            : apiVersion == parsedExpectedVersion
                ? CompatibilityLevel.Full
                : CompatibilityLevel.Compatible;
        return Task.FromResult(new ServerApiInfo(
            compatibilityLevel,
            ApiConstants.VersionString,
            ApiConstants.FullVersionString,
            ApiConstants.DisplayVersionString,
            MinReportableClientVersion.ToString()));
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
                ComputedRegistry.InvalidateEverything();
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

        // We must call CreateOperationDbContext to make sure this operation is logged in the User DB
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
    }
}
