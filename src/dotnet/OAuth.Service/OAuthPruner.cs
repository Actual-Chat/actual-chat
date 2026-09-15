using ActualChat.OAuth.Module;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth;

/// <summary>
/// Hourly cleanup of the OAuth store: dead tokens, DCR clients nobody consented to within
/// <see cref="OAuthSettings.DcrPruneAge"/>, and grants whose backing session is gone or inactive.
/// Every pass is idempotent, so hosts run it independently.
/// </summary>
public sealed class OAuthPruner : WorkerBase
{
    private static readonly RandomTimeSpan Period = TimeSpan.FromHours(1).ToRandom(0.25);
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TokenRetention = TimeSpan.FromDays(1);
    // OAuthGrants.OnApprove creates the authorization before its backing session, so a grant this young
    // with no session row is a consent in flight, not a dead grant
    private static readonly TimeSpan NewGrantGrace = TimeSpan.FromMinutes(5);
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(30, 600);

    private IServiceProvider Services { get; }
    private OAuthGrants Grants { get; }
    private ISessionsBackend SessionsBackend { get; }
    private OAuthSettings Settings { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public OAuthPruner(IServiceProvider services)
    {
        Services = services;
        Grants = services.GetRequiredService<OAuthGrants>();
        SessionsBackend = services.GetRequiredService<ISessionsBackend>();
        Settings = services.GetRequiredService<OAuthSettings>();
        Clocks = services.Clocks();
        Log = services.LogFor(GetType());
    }

    public async Task RunOnce(CancellationToken cancellationToken)
    {
        // The managers are scoped, and an entity must be mutated in the scope that loaded it
        using var scope = Services.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var tokens = scopedServices.GetRequiredService<IOpenIddictTokenManager>();
        var now = Clocks.SystemClock.Now;
        var prunedTokenCount = await tokens.PruneAsync((now - TokenRetention).ToDateTimeOffset(), cancellationToken)
            .ConfigureAwait(false);
        if (prunedTokenCount > 0)
            Log.LogInformation("Pruned {Count} dead tokens", prunedTokenCount);

        await PruneOrphanClients(scopedServices, now, cancellationToken).ConfigureAwait(false);
        await RevokeDeadGrants(scopedServices, now, cancellationToken).ConfigureAwait(false);
    }

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
        // PrependDelay wraps the cycling chain, so it staggers the host once rather than every cycle
        => AsyncChain.From(RunOnce)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelays, Log)
            .AppendDelay(Period, Clocks.CpuClock)
            .CycleForever()
            .PrependDelay(FirstDelay, Clocks.CpuClock)
            .Run(cancellationToken);

    // It's internal so tests can age a registration past DcrPruneAge
    internal async Task MarkRegisteredAt(string clientId, DateTime registeredAt, CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.NotFound<OAuthClientInfo>("OAuth client is not found.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, application, cancellationToken).ConfigureAwait(false);
        descriptor.Properties[OAuthConstants.Properties.RegisteredAt] = JsonSerializer.SerializeToElement(registeredAt);
        await applications.UpdateAsync(application, descriptor, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task PruneOrphanClients(
        IServiceProvider scopedServices, Moment now, CancellationToken cancellationToken)
    {
        // The store streams ListAsync over an open reader, and Npgsql runs one command per connection,
        // so the list is materialized before anything else touches the scope's DbContext
        var applications = scopedServices.GetRequiredService<IOpenIddictApplicationManager>();
        var authorizations = scopedServices.GetRequiredService<IOpenIddictAuthorizationManager>();
        var cutoff = (now - Settings.DcrPruneAge).ToDateTime();
        var all = await applications.ListAsync(cancellationToken: cancellationToken).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var application in all) {
            var properties = await applications.GetPropertiesAsync(application, cancellationToken)
                .ConfigureAwait(false);
            if (!properties.TryGetValue(OAuthConstants.Properties.RegisteredVia, out var via)
                || via.ValueKind != JsonValueKind.String
                || via.GetString() != OAuthConstants.RegisteredVia.Dcr)
                continue;
            if (!properties.TryGetValue(OAuthConstants.Properties.RegisteredAt, out var at)
                || !at.TryGetDateTime(out var registeredAt)
                || registeredAt > cutoff)
                continue;

            var id = (await applications.GetIdAsync(application, cancellationToken).ConfigureAwait(false))!;
            var hasGrant = await authorizations.FindByApplicationIdAsync(id, cancellationToken)
                .AnyAsync(cancellationToken)
                .ConfigureAwait(false);
            if (hasGrant)
                continue;

            var clientId = await applications.GetClientIdAsync(application, cancellationToken).ConfigureAwait(false);
            await applications.DeleteAsync(application, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Pruned orphan DCR client {ClientId}", clientId);
        }
    }

    private async Task RevokeDeadGrants(
        IServiceProvider scopedServices, Moment now, CancellationToken cancellationToken)
    {
        var authorizations = scopedServices.GetRequiredService<IOpenIddictAuthorizationManager>();
        var graceCutoff = (now - NewGrantGrace).ToDateTimeOffset();
        var all = await authorizations.ListAsync(cancellationToken: cancellationToken).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var authorization in all) {
            var status = await authorizations.GetStatusAsync(authorization, cancellationToken).ConfigureAwait(false);
            if (status != Statuses.Valid)
                continue;

            var sessionId = await Grants.GetSessionId(scopedServices, authorization, cancellationToken)
                .ConfigureAwait(false);
            var sessionInfo = sessionId is null
                ? null
                : await SessionsBackend.Get(new Session(sessionId), cancellationToken).ConfigureAwait(false);
            if (sessionInfo is { IsActive: true })
                continue;
            if (sessionId is not null && sessionInfo is null) {
                var createdAt = await authorizations.GetCreationDateAsync(authorization, cancellationToken)
                    .ConfigureAwait(false);
                if (createdAt > graceCutoff)
                    continue;
            }

            var id = await authorizations.GetIdAsync(authorization, cancellationToken).ConfigureAwait(false);
            await Grants.Revoke(scopedServices, authorization, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Revoked authorization {AuthorizationId}: session {SessionId} is dead", id, sessionId);
        }
    }
}
