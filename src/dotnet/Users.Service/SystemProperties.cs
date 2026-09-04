using System.Security.Cryptography;
using ActualChat.Diagnostics;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.Internal;
using ActualLab.Rpc;

namespace ActualChat.Users;

public class SystemProperties(IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), ISystemProperties
{
    private const int MinProbePayloadSize = 1024;
    private const int MaxProbePayloadSize = 256 * 1024;
    private const string EdgeHostInfix = ".edge.";
    private const int MaxEdgeNameLength = 16;
    private const double MaxProbeDurationMs = 600_000;
    private static readonly Version MinCompatibleVersion = new(2, 15);
    private static readonly Version MinReportableClientVersion = MinCompatibleVersion;
    // Normalized the same way as the client version, so an X.Y match means CompatibilityLevel.Full
    private static readonly Version ApiVersion = VersionExt.ParseBuildVersion(ApiConstants.VersionString);
    private HostInfo HostInfo => field ??= Services.HostInfo();

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

    public Task ReportRpcEndpoint(RpcEndpointReport report, CancellationToken cancellationToken)
    {
        var hosts = HostInfo.GetHosts();
        var endpoint = EndpointTag(hosts, report.Endpoint);
        var reason = Enum.IsDefined(report.Reason) ? report.Reason : RpcEndpointReason.Retained;
        AppMeters.RpcEndpointConnectionCount.Add(1,
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("reason", reason.ToString().ToLower()));
        RecordProbeDuration(report.OriginProbeMs, EndpointTag(hosts, HostInfo.BaseUrl.ToUri().Host), "origin");
        RecordProbeDuration(report.EndpointProbeMs, endpoint, "selected");
        return RpcNoWait.Tasks.Completed;
    }

    // [ComputeMethod]
    public virtual Task<ServerApiInfo> GetServerApiInfo(string expectedVersion, CancellationToken cancellationToken)
        => GetServerApiInfoNC(expectedVersion, cancellationToken);

    // Not a [ComputeMethod]!
    public Task<ServerApiInfo> GetServerApiInfoNC(string expectedVersion, CancellationToken cancellationToken)
    {
        if (!VersionExt.TryParseBuildVersion(expectedVersion, out var parsedExpectedVersion))
            return Task.FromResult(new ServerApiInfo(CompatibilityLevel.Unknown));

        var apiVersion = ApiVersion;
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

    // Protected/internal methods

    // It's internal to be accessible from tests
    internal static string EndpointTag(IReadOnlySet<string> knownHosts, string endpoint)
    {
        // The tag comes from a client, so it can't be passed through: an unbounded tag value
        // is an unbounded number of time series. Only the relay's own name survives, and
        // anything unrecognized collapses into one bucket.
        if (endpoint.IsNullOrEmpty())
            return "other";
        if (knownHosts.Contains(endpoint))
            return "origin";

        var edgeAt = endpoint.IndexOf(EdgeHostInfix, StringComparison.OrdinalIgnoreCase);
        if (edgeAt <= 0)
            return "other";

        var name = endpoint[..edgeAt];
        return name.Length <= MaxEdgeNameLength && name.All(char.IsAsciiLetterOrDigit)
            ? "edge:" + name.ToLower()
            : "other";
    }

    // Private methods

    private static void RecordProbeDuration(double elapsedMs, string endpoint, string role)
    {
        if (double.IsNaN(elapsedMs) || elapsedMs < 0 || elapsedMs > MaxProbeDurationMs)
            return;

        AppMeters.RpcEndpointProbeDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("role", role));
    }
}
