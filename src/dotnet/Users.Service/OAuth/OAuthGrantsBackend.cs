using ActualChat.Db;
using ActualChat.OAuth.Db;
using ActualChat.OAuth.Module;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Interception;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth;

public class OAuthGrantsBackend(IServiceProvider services)
    : DbServiceBase<OAuthDbContext>(services), IOAuthGrantsBackend
{
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private ISessionsBackend SessionsBackend { get; } = services.GetRequiredService<ISessionsBackend>();
    private OAuthSettings Settings { get; } = services.GetRequiredService<OAuthSettings>();

    [ComputeMethod]
    public virtual async Task<ApiArray<OAuthGrant>> List(UserId userId, CancellationToken cancellationToken)
    {
        var sessions = await AccountsBackend.ListSessions(userId, cancellationToken).ConfigureAwait(false);
        var sessionIds = sessions.Where(s => s.Kind == SessionKind.OAuth).Select(s => s.Id).ToHashSet();
        using var scope = Services.CreateScope();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var result = new List<OAuthGrant>();
        var userAuthorizations = authorizations.FindBySubjectAsync(userId.Value, cancellationToken);
        await foreach (var authorization in userAuthorizations.ConfigureAwait(false)) {
            var status = await authorizations.GetStatusAsync(authorization, cancellationToken).ConfigureAwait(false);
            if (status != Statuses.Valid)
                continue;

            var sessionId = await GetSessionId(scope.ServiceProvider, authorization, cancellationToken)
                .ConfigureAwait(false);
            if (sessionId is null || !sessionIds.Contains(sessionId))
                continue;

            var sessionInfo = await SessionsBackend.Get(new Session(sessionId), cancellationToken)
                .ConfigureAwait(false);
            if (sessionInfo is not { IsActive: true })
                continue;

            var applicationId = await authorizations.GetApplicationIdAsync(authorization, cancellationToken)
                .ConfigureAwait(false);
            var application = applicationId is null
                ? null
                : await applications.FindByIdAsync(applicationId, cancellationToken).ConfigureAwait(false);
            var clientId = application is null
                ? ""
                : await applications.GetClientIdAsync(application, cancellationToken).ConfigureAwait(false) ?? "";
            var clientName = application is null
                ? clientId
                : await applications.GetDisplayNameAsync(application, cancellationToken).ConfigureAwait(false)
                    ?? clientId;
            var id = (await authorizations.GetIdAsync(authorization, cancellationToken).ConfigureAwait(false))!;
            var scopes = await authorizations.GetScopesAsync(authorization, cancellationToken).ConfigureAwait(false);
            var createdAt = await authorizations.GetCreationDateAsync(authorization, cancellationToken)
                .ConfigureAwait(false);
            result.Add(new OAuthGrant(
                id,
                clientId,
                clientName,
                scopes.ToApiArray(),
                createdAt?.ToMoment() ?? sessionInfo.CreatedAt,
                sessionInfo.LastSeenAt,
                sessionInfo.ExpiresAt));
        }
        return result.OrderByDescending(g => g.CreatedAt).ToApiArray();
    }

    public virtual async Task<OAuthClientInfo?> GetClient(
        string clientId, CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (application is null)
            return null;

        var redirectUris = await applications.GetRedirectUrisAsync(application, cancellationToken)
            .ConfigureAwait(false);
        var permissions = await applications.GetPermissionsAsync(application, cancellationToken).ConfigureAwait(false);
        var clientName = await applications.GetDisplayNameAsync(application, cancellationToken).ConfigureAwait(false);
        var uris = redirectUris.Select(u => new Uri(u)).ToList();
        var scopes = permissions
            .Where(p => p.StartsWith(Permissions.Prefixes.Scope))
            .Select(p => p[Permissions.Prefixes.Scope.Length..])
            .Append(Scopes.OfflineAccess)
            .ToApiArray();
        return new OAuthClientInfo(
            clientId,
            clientName ?? clientId,
            uris.Select(u => u.Host).Distinct().ToApiArray(),
            uris.Any(OAuthApplications.IsLoopback),
            scopes);
    }

    public virtual async Task<string> Approve(
        UserId userId, string clientId, ApiArray<string> requestedScopes, CancellationToken cancellationToken)
    {
        var scopes = requestedScopes
            .Where(s => s == OAuthConstants.McpScope || s == Scopes.OfflineAccess)
            .Distinct()
            .ToArray();
        if (!scopes.Contains(OAuthConstants.McpScope))
            throw StandardError.Constraint($"The '{OAuthConstants.McpScope}' scope is required.");

        using var scope = Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var application = await applications.FindByClientIdAsync(clientId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw StandardError.NotFound<OAuthClientInfo>("OAuth client is not found.");
        var applicationId = (await applications.GetIdAsync(application, cancellationToken).ConfigureAwait(false))!;
        var clientName = await applications.GetDisplayNameAsync(application, cancellationToken).ConfigureAwait(false)
            ?? clientId;

        // Keep the pre-split lock key so old and new nodes serialize concurrent consents together.
        // This transaction holds only the lock; OpenIddict manages the writes in its own scope.
        var lockDbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _1 = lockDbContext.ConfigureAwait(false);
        var lockTransaction = await lockDbContext.Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var _2 = lockTransaction.ConfigureAwait(false);
        var lockKey = GetConsentLockKey(userId, applicationId);
        await lockDbContext.Lock(lockKey, cancellationToken).ConfigureAwait(false);

        var existing = await FindAuthorization(
            scope.ServiceProvider, userId, applicationId, scopes, cancellationToken).ConfigureAwait(false);
        if (existing is not null) {
            // A grant whose backing session expired is dead for good (nothing can issue tokens for it),
            // so it's replaced rather than reused.
            var existingSessionId = await GetSessionId(scope.ServiceProvider, existing, cancellationToken)
                .ConfigureAwait(false);
            var existingSessionInfo = existingSessionId is null
                ? null
                : await SessionsBackend.Get(new Session(existingSessionId), cancellationToken).ConfigureAwait(false);
            if (existingSessionInfo is { IsActive: true })
                return (await authorizations.GetIdAsync(existing, cancellationToken).ConfigureAwait(false))!;

            await Revoke(scope.ServiceProvider, existing, cancellationToken).ConfigureAwait(false);
        }

        var backingSession = SessionExt.NewOAuth();
        var descriptor = new OpenIddictAuthorizationDescriptor {
            ApplicationId = applicationId,
            Subject = userId.Value,
            Status = Statuses.Valid,
            Type = AuthorizationTypes.Permanent,
            CreationDate = Clocks.SystemClock.Now.ToDateTimeOffset(),
        };
        foreach (var s in scopes)
            descriptor.Scopes.Add(s);
        descriptor.Properties[OAuthConstants.Properties.SessionId]
            = JsonSerializer.SerializeToElement(backingSession.Id);
        var authorization = await authorizations.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);

        await Commander.Call(new SessionsBackend_Upsert(backingSession) {
            UserId = userId,
            Description = $"{clientName} (OAuth)",
            ExpiresAt = Clocks.SystemClock.Now + Settings.RefreshTokenLifetime,
        }, true, cancellationToken).ConfigureAwait(false);

        var authorizationId = (await authorizations.GetIdAsync(authorization, cancellationToken)
            .ConfigureAwait(false))!;
        var survivorId = await Converge(scope.ServiceProvider, userId, applicationId, scopes, cancellationToken)
            .ConfigureAwait(false)
            ?? authorizationId;
        if (survivorId != authorizationId)
            await Commander.Call(new AccountsBackend_SignOut(backingSession, Deactivate: true), true, cancellationToken)
                .ConfigureAwait(false);
        return survivorId;
    }

    public virtual async Task Revoke(
        UserId userId, string authorizationId, CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await authorizations.FindByIdAsync(authorizationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw StandardError.NotFound<OAuthGrant>("OAuth grant is not found.");
        var subject = await authorizations.GetSubjectAsync(authorization, cancellationToken).ConfigureAwait(false);
        if (subject != userId.Value)
            throw StandardError.NotFound<OAuthGrant>("OAuth grant is not found.");

        await Revoke(scope.ServiceProvider, authorization, cancellationToken).ConfigureAwait(false);
    }

    public async Task<object?> FindAuthorization(
        UserId userId, string applicationId, IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
    {
        // The result is detached from its scope: read it (GetSessionId, GetIdAsync), but re-load it by id
        // in your own scope before mutating it — an entity attached across scopes fails to update.
        using var scope = Services.CreateScope();
        return await FindAuthorization(scope.ServiceProvider, userId, applicationId, scopes, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task Revoke(IServiceProvider scopedServices, object authorization, CancellationToken cancellationToken)
    {
        var authorizations = scopedServices.GetRequiredService<IOpenIddictAuthorizationManager>();
        var tokens = scopedServices.GetRequiredService<IOpenIddictTokenManager>();
        var authorizationId = (await authorizations.GetIdAsync(authorization, cancellationToken)
            .ConfigureAwait(false))!;
        await authorizations.TryRevokeAsync(authorization, cancellationToken).ConfigureAwait(false);
        var authorizationTokens = tokens.FindByAuthorizationIdAsync(authorizationId, cancellationToken);
        await foreach (var token in authorizationTokens.ConfigureAwait(false))
            await tokens.TryRevokeAsync(token, cancellationToken).ConfigureAwait(false);
        var sessionId = await GetSessionId(scopedServices, authorization, cancellationToken).ConfigureAwait(false);
        if (sessionId is null)
            return;

        // Signing out a session whose row is already gone would upsert a junk expired row
        var session = new Session(sessionId);
        var sessionInfo = await SessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo is not null)
            await Commander.Call(new AccountsBackend_SignOut(session, Deactivate: true), true, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<string?> GetSessionId(
        IServiceProvider scopedServices, object authorization, CancellationToken cancellationToken)
    {
        var authorizations = scopedServices.GetRequiredService<IOpenIddictAuthorizationManager>();
        var properties = await authorizations.GetPropertiesAsync(authorization, cancellationToken)
            .ConfigureAwait(false);
        return properties.TryGetValue(OAuthConstants.Properties.SessionId, out var sessionId)
            ? sessionId.GetString()
            : null;
    }

    public async Task TouchAuthorization(string authorizationId, CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await authorizations.FindByIdAsync(authorizationId, cancellationToken)
            .ConfigureAwait(false);
        if (authorization is null)
            return;

        var sessionId = await GetSessionId(scope.ServiceProvider, authorization, cancellationToken)
            .ConfigureAwait(false);
        if (sessionId is null)
            return;

        await Commander.Call(new SessionsBackend_Upsert(new Session(sessionId)) {
            ExpiresAt = Clocks.SystemClock.Now + Settings.RefreshTokenLifetime,
        }, true, cancellationToken).ConfigureAwait(false);
    }

    internal static DbLockKey GetConsentLockKey(UserId userId, string applicationId)
        => DbLockKey.New(Proxies.GetProxyType(typeof(OAuthGrants)), userId, applicationId);

    // Private methods

    private static async Task<object?> FindAuthorization(
        IServiceProvider scopedServices, UserId userId, string applicationId,
        IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
    {
        var authorizations = scopedServices.GetRequiredService<IOpenIddictAuthorizationManager>();
        var matches = FindAuthorizations(authorizations, userId, applicationId, scopes, cancellationToken);
        return await matches.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> Converge(
        IServiceProvider scopedServices, UserId userId, string applicationId,
        IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
    {
        // Collapses duplicates created before advisory locking into the oldest valid permanent grant.
        // Returns its id, or null if no valid grant remains.
        var authorizations = scopedServices.GetRequiredService<IOpenIddictAuthorizationManager>();
        var candidates = new List<(DateTimeOffset CreatedAt, string Id, object Authorization)>();
        var matches = FindAuthorizations(authorizations, userId, applicationId, scopes, cancellationToken);
        await foreach (var authorization in matches.ConfigureAwait(false)) {
            var id = (await authorizations.GetIdAsync(authorization, cancellationToken).ConfigureAwait(false))!;
            var createdAt = await authorizations.GetCreationDateAsync(authorization, cancellationToken)
                .ConfigureAwait(false);
            candidates.Add((createdAt ?? DateTimeOffset.MaxValue, id, authorization));
        }
        if (candidates.Count == 0)
            return null;

        var survivor = candidates.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).First();
        foreach (var candidate in candidates.Where(c => c.Id != survivor.Id))
            await Revoke(scopedServices, candidate.Authorization, cancellationToken).ConfigureAwait(false);
        return survivor.Id;
    }

    private static IAsyncEnumerable<object> FindAuthorizations(
        IOpenIddictAuthorizationManager authorizations, UserId userId, string applicationId,
        IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
        => authorizations.FindAsync(
            userId.Value, applicationId, Statuses.Valid, AuthorizationTypes.Permanent,
            scopes.ToImmutableArray(), cancellationToken);
}
