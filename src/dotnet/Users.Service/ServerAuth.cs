using System.Security.Claims;
using ActualChat.AspNetCore;
using ActualChat.Hosting;
using Microsoft.AspNetCore.Http;

namespace ActualChat.Users;

public sealed class ServerAuth
{
    public string[] IdClaimKeys { get; init; } = [ClaimTypes.NameIdentifier];
    public string[] NameClaimKeys { get; init; } = [];
    public string CloseFlowRequestPath { get; init; } = "/fusion/close";
    public string AppCloseFlowRequestPath { get; init; } = "/fusion/close-app";
    public TimeSpan SessionInfoUpdatePeriod { get; init; } = Constants.Session.LastSeenAtUpdatePeriod;
    public Func<ServerAuth, HttpContext, bool> AllowSignIn = AllowOnCloseFlow;
    public Func<ServerAuth, HttpContext, bool> AllowChange = AllowOnCloseFlow;
    public Func<ServerAuth, HttpContext, bool> AllowSignOut = AllowOnCloseFlow;

    public HostInfo HostInfo { get; }
    public IAccounts Accounts { get; }
    public IAccountsBackend AccountsBackend { get; }
    public ISessionsBackend SessionsBackend { get; }
    public ICommander Commander { get; }
    public MomentClockSet Clocks { get; }

    private ClaimMapper ClaimMapper { get; }
    private ILogger Log { get; }

    public ServerAuth(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();

        HostInfo = services.HostInfo();
        Accounts = services.GetRequiredService<IAccounts>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        SessionsBackend = services.GetRequiredService<ISessionsBackend>();
        ClaimMapper = services.GetRequiredService<ClaimMapper>();
        Commander = services.Commander();

        if (HostInfo.IsDevelopmentInstance)
            AllowSignIn = AllowAnywhere;
    }

    public CloseFlowInfo? IsCloseFlow(HttpContext httpContext)
    {
        var request = httpContext.Request;
        if (request.Path.Value != CloseFlowRequestPath
            && request.Path.Value != AppCloseFlowRequestPath)
            return null;

        var name = "";
        if (request.Query.TryGetValue("flow", out var flowValues))
            name = (flowValues.FirstOrDefault() ?? "").Capitalize();
        if (name.IsNullOrEmpty())
            return null;

        string? redirectUrl = null;
        if (request.Query.TryGetValue("redirectUrl", out var returnUrlValues))
            redirectUrl = returnUrlValues.FirstOrDefault().NullIfEmpty();

        var mustClose = true;
        if (request.Query.TryGetValue("mustClose", out var mustCloseValues))
            mustClose = int.TryParse(mustCloseValues.FirstOrDefault(), out var x) && x != 0;
        return new CloseFlowInfo(name, redirectUrl, mustClose);
    }

    public Task<(Session Session, bool IsNew)> Authenticate(
        HttpContext httpContext, CancellationToken cancellationToken)
        => Authenticate(httpContext, false, false, cancellationToken);
    public Task<(Session Session, bool IsNew)> Authenticate(
        HttpContext httpContext, bool assumeAllowed,
        CancellationToken cancellationToken = default)
        => Authenticate(httpContext, assumeAllowed, false, cancellationToken);
    public async Task<(Session Session, bool IsNew)> Authenticate(
        HttpContext httpContext, bool assumeAllowed, bool mustExist,
        CancellationToken cancellationToken = default)
    {
        var cookieSession = httpContext.TryGetSessionFromCookie();
        if (!await Accounts.IsValidSession(cookieSession, cancellationToken).ConfigureAwait(false))
            cookieSession = null;

        var session = cookieSession ?? Session.New();
        for (var tryIndex = 0;; tryIndex++) {
            try {
#if false
                // You can enable this code to verify this logic works
                if (Random.Shared.Next(3) == 0) {
                    await Task.Delay(1000).ConfigureAwait(false);
                    throw new TimeoutException();
                }
#endif
                await UpdateAuthState(session, httpContext, assumeAllowed, mustExist, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                    .ConfigureAwait(false);
                var isNew = cookieSession != session;
                if (isNew)
                    httpContext.AddSessionCookie(session);
                return (session, isNew);
            }
            catch (TimeoutException) {
                if (tryIndex >= 2)
                    throw;
            }
            catch (InvalidOperationException e)
                when (e.Message.Contains("Inactive session", StringComparison.Ordinal)) {
                Log.LogWarning(e, "Session is broken, creating a new one");
                if (tryIndex >= 2)
                    throw;
            }
            session = Session.New();
        }
    }

    public Task UpdateAuthState(
        Session session, HttpContext httpContext, bool assumeAllowed,
        CancellationToken cancellationToken)
        => UpdateAuthState(session, httpContext, assumeAllowed, false, cancellationToken);

    public async Task UpdateAuthState(
        Session session, HttpContext httpContext, bool assumeAllowed, bool mustExist,
        CancellationToken cancellationToken)
    {
        if (session.Kind is SessionKind.ApiKey)
            throw StandardError.Unavailable("Cannot use API key session here.");

        var httpUser = httpContext.User;
        var httpAuthenticationSchema = httpUser.Identity?.AuthenticationType ?? "";
        var httpIsSignedIn = !httpAuthenticationSchema.IsNullOrEmpty();

        var ipAddress = httpContext.GetRemoteIPAddress()?.ToString() ?? "";
        var description = httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgentValues)
            ? userAgentValues.FirstOrDefault() ?? ""
            : "";

        var sessionInfo = await Accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var mustSetupSession =
            sessionInfo == null
            || sessionInfo.IPAddress != ipAddress
            || sessionInfo.Description != description
            || sessionInfo.LastSeenAt + SessionInfoUpdatePeriod < Clocks.SystemClock.Now;
        if (mustSetupSession || sessionInfo == null) {
            var upsertSessionCmd = new SessionsBackend_Upsert(session) {
                IPAddress = ipAddress,
                Description = description,
            };
            await Commander.Call(upsertSessionCmd, true, cancellationToken).ConfigureAwait(false);
        }

        var existingAccount = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var isSignedIn = !existingAccount.IsGuest;
        if (!isSignedIn)
            existingAccount = null;

        if (httpIsSignedIn) {
            if (isSignedIn && IsSameAccount(existingAccount, httpUser, httpAuthenticationSchema))
                return; // Nothing to change

            var isSignInAllowed = !isSignedIn
                ? assumeAllowed || AllowSignIn(this, httpContext)
                : assumeAllowed || AllowChange(this, httpContext);
            if (!isSignInAllowed)
                return; // Sign-in or user change is not allowed for the current location

            await SignIn(session, existingAccount, httpUser, httpAuthenticationSchema, mustExist, cancellationToken).ConfigureAwait(false);
        }
        else if (isSignedIn && (assumeAllowed || AllowSignOut(this, httpContext)))
            await SignOut(session, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task SignIn(
        Session session, AccountFull? existingAccount, ClaimsPrincipal httpUser, string httpAuthenticationSchema,
        bool mustExist, CancellationToken cancellationToken)
    {
        var signInCommand = await GetSignInCommand(session, httpUser, httpAuthenticationSchema, mustExist, cancellationToken).ConfigureAwait(false);
        await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private Task SignOut(Session session, CancellationToken cancellationToken)
    {
        var signOutCommand = new AccountsBackend_SignOut(session);
        return Commander.Call(signOutCommand, true, cancellationToken);
    }

    private bool IsSameAccount(AccountFull? account, ClaimsPrincipal httpUser, string schema)
    {
        if (account == null)
            return false;

        var httpUserIdentityName = httpUser.Identity?.Name ?? "";
        var claims = httpUser.Claims.ToImmutableDictionary(c => c.Type, c => c.Value);
        var id = FirstClaimOrDefault(claims, IdClaimKeys) ?? httpUserIdentityName;
        var identity = new UserIdentity(schema, id);
        return account.Identities.ContainsKey(identity);
    }

    private async Task<AccountsBackend_SignIn> GetSignInCommand(
        Session session, ClaimsPrincipal httpUser, string schema, bool mustExist, CancellationToken cancellationToken)
    {
        var httpUserIdentityName = httpUser.Identity?.Name ?? "";
        var claims = httpUser.Claims.ToApiMap(c => c.Type, c => c.Value);
        var id = FirstClaimOrDefault(claims, IdClaimKeys) ?? httpUserIdentityName;
        var identity = new UserIdentity(schema, id);
        var identities = ApiMap<UserIdentity, string>.Empty;

        // Map claims using ClaimMapper
        var httpClaims = httpUser.Claims.ToDictionary(c => c.Type, c => c.Value);
        (claims, _) = ClaimMapper.UpdateClaims(claims, httpClaims);

        // For external providers, try to link by email if the identity doesn't exist yet
        var authenticatedIdentity = identity;
        if (!AuthSchema.IsExternal(schema))
            goto exit;

        var existingUserId = await AccountsBackend.GetIdByUserIdentity(identity, cancellationToken).ConfigureAwait(false);
        if (existingUserId is not null
            || httpUser.FindFirstValue(ClaimTypes.Email) is not { } emailClaim
            || !ActualChat.Email.TryParse(emailClaim, out var email))
            goto exit;

        var emailHash = email.Hash;
        var userId = await AccountsBackend.GetUserIdByEmailHash(emailHash, cancellationToken).ConfigureAwait(false);
        if (userId is null)
            goto exit;

        // Found existing user by email - use email identity as authenticated identity
        authenticatedIdentity = UserIdentityExt.NewEmailIdentity(email);
        identities = identities.With(identity, "");

        exit:
        return new AccountsBackend_SignIn(session, authenticatedIdentity, identities, claims, mustExist);
    }

    private static string? FirstClaimOrDefault(IReadOnlyDictionary<string, string> claims, string[] keys)
    {
        foreach (var key in keys)
            if (claims.TryGetValue(key, out var value) && !value.IsNullOrEmpty())
                return value;
        return null;
    }

    // AllowXxx

    private static bool AllowAnywhere(ServerAuth h, HttpContext httpContext)
        => true;

    private static bool AllowOnCloseFlow(ServerAuth h, HttpContext httpContext)
        => h.IsCloseFlow(httpContext) != null;
}
