using System.Security.Claims;
using ActualChat.Hosting;
using Microsoft.AspNetCore.Http;
using ActualLab.Fusion.Server.Authentication;

namespace ActualChat.Users;

public sealed class ServerAuth
{
    public string[] IdClaimKeys { get; init; } = [ClaimTypes.NameIdentifier];
    public string[] NameClaimKeys { get; init; } = [];
    public string CloseFlowRequestPath { get; init; } = "/fusion/close";
    public string AppCloseFlowRequestPath { get; init; } = "/fusion/close-app";
    public TimeSpan SessionInfoUpdatePeriod { get; init; } = Constants.Session.SessionInfoUpdatePeriod;
    public Func<ServerAuth, HttpContext, bool> AllowSignIn = AllowOnCloseFlow;
    public Func<ServerAuth, HttpContext, bool> AllowChange = AllowOnCloseFlow;
    public Func<ServerAuth, HttpContext, bool> AllowSignOut = AllowOnCloseFlow;

    public HostInfo HostInfo { get; }
    public IAccounts Accounts { get; }
    public IAccountsBackend AccountsBackend { get; }
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
        ClaimMapper = services.GetRequiredService<ClaimMapper>();
        Commander = services.Commander();

        if (HostInfo.IsDevelopmentInstance)
            AllowSignIn = AllowAnywhere;
    }

    public CloseFlowInfo? IsCloseFlow(HttpContext httpContext)
    {
        var request = httpContext.Request;
        if (!OrdinalEquals(request.Path.Value, CloseFlowRequestPath)
            && !OrdinalEquals(request.Path.Value, AppCloseFlowRequestPath))
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
            mustClose = int.TryParse(mustCloseValues.FirstOrDefault(), CultureInfo.InvariantCulture, out var x) && x != 0;
        return new CloseFlowInfo(name, redirectUrl, mustClose);
    }

    public Task<(Session Session, bool IsNew)> Authenticate(
        HttpContext httpContext, CancellationToken cancellationToken)
        => Authenticate(httpContext, false, cancellationToken);
    public async Task<(Session Session, bool IsNew)> Authenticate(
        HttpContext httpContext, bool assumeAllowed,
        CancellationToken cancellationToken = default)
    {
        var originalSession = httpContext.TryGetSessionFromCookie();
        var session = originalSession ?? Session.New();
        for (var tryIndex = 0;; tryIndex++) {
            try {
#if false
                // You can enable this code to verify this logic works
                if (Random.Shared.Next(3) == 0) {
                    await Task.Delay(1000).ConfigureAwait(false);
                    throw new TimeoutException();
                }
#endif
                await UpdateAuthState(session, httpContext, assumeAllowed, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                    .ConfigureAwait(false);
                var isNew = originalSession != session;
                if (isNew)
                    httpContext.AddSessionCookie(session);
                return (session, isNew);
            }
            catch (TimeoutException) {
                if (tryIndex >= 2)
                    throw;
            }
            session = Session.New();
        }
    }

    public async Task UpdateAuthState(
        Session session, HttpContext httpContext, bool assumeAllowed,
        CancellationToken cancellationToken)
    {
        var httpUser = httpContext.User;
        var httpAuthenticationSchema = httpUser.Identity?.AuthenticationType ?? "";
        var httpIsSignedIn = !httpAuthenticationSchema.IsNullOrEmpty();

        var ipAddress = httpContext.GetRemoteIPAddress()?.ToString() ?? "";
        var userAgent = httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgentValues)
            ? userAgentValues.FirstOrDefault() ?? ""
            : "";

        var sessionInfo = await Accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var mustSetupSession =
            sessionInfo == null
            || !OrdinalEquals(sessionInfo.IPAddress, ipAddress)
            || !OrdinalEquals(sessionInfo.UserAgent, userAgent)
            || sessionInfo.LastSeenAt + SessionInfoUpdatePeriod < Clocks.SystemClock.Now;
        if (mustSetupSession || sessionInfo == null) {
            var upsertSessionCmd = new SessionsBackend_Upsert(session, ipAddress, userAgent);
            await Commander.Call(upsertSessionCmd, true, cancellationToken).ConfigureAwait(false);
        }

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var isSignedIn = !account.IsGuest;
        AccountFull? existingAccount = isSignedIn ? account : null;

        try {
            if (httpIsSignedIn) {
                if (isSignedIn && IsSameAccount(existingAccount, httpUser, httpAuthenticationSchema))
                    return; // Nothing to change

                var isSignInAllowed = !isSignedIn
                    ? assumeAllowed || AllowSignIn(this, httpContext)
                    : assumeAllowed || AllowChange(this, httpContext);
                if (!isSignInAllowed)
                    return; // Sign-in or user change is not allowed for the current location

                await SignIn(session, existingAccount, httpUser, httpAuthenticationSchema, cancellationToken).ConfigureAwait(false);
            }
            else if (isSignedIn && (assumeAllowed || AllowSignOut(this, httpContext)))
                await SignOut(session, cancellationToken).ConfigureAwait(false);
        }
        finally {
            // This should be done once important things are completed
            _ = Accounts.UpdatePresence(session, CancellationToken.None);
        }
    }

    // Private methods

    private async Task SignIn(
        Session session, AccountFull? existingAccount, ClaimsPrincipal httpUser, string httpAuthenticationSchema,
        CancellationToken cancellationToken)
    {
        var (account, authenticatedIdentity) =
            await CreateOrUpdateAccount(existingAccount, httpUser, httpAuthenticationSchema, cancellationToken).ConfigureAwait(false);

        var signInCommand = new AccountsBackend_SignIn(session, account, authenticatedIdentity);
        await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private Task SignOut(Session session, CancellationToken cancellationToken)
    {
        var signOutCommand = new SessionsBackend_SignOut(session);
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

    private async Task<(AccountFull Account, UserIdentity AuthenticatedIdentity)> CreateOrUpdateAccount(
        AccountFull? existingAccount, ClaimsPrincipal httpUser, string schema,
        CancellationToken cancellationToken)
    {
        var (account, userIdentity) = BaseCreateOrUpdateAccount(existingAccount, httpUser, schema);
        var httpClaims = httpUser.Claims.ToDictionary(c => c.Type, c => c.Value, StringComparer.Ordinal);
        account = ClaimMapper.UpdateClaims(account, httpClaims);
        await UseExistingEmailIdentity().ConfigureAwait(false);
        return (account, userIdentity);

        async Task UseExistingEmailIdentity()
        {
            var existingUserId = await AccountsBackend.GetIdByUserIdentity(userIdentity, cancellationToken).ConfigureAwait(false);
            // Check if a user with such email exists when logging in with external identity
            if (existingUserId is not null || !AuthSchema.IsExternal(schema) || httpUser.FindFirstValue(ClaimTypes.Email) is not { } emailString)
                return;

            if (!ActualChat.Email.TryParse(emailString, out var email))
                return;

            var emailHash = email.Hash;
            var userId = await AccountsBackend.GetIdByEmailHash(emailHash, cancellationToken)
                .ConfigureAwait(false);
            if (userId is null)
                return;

            account = account.WithEmailIdentities(email);
            userIdentity = account.GetEmailIdentity();
        }
    }

    private (AccountFull Account, UserIdentity AuthenticatedIdentity) BaseCreateOrUpdateAccount(
        AccountFull? existingAccount, ClaimsPrincipal httpUser, string schema)
    {
        var httpUserIdentityName = httpUser.Identity?.Name ?? "";
        var claims = httpUser.Claims.ToApiMap(c => c.Type, c => c.Value, StringComparer.Ordinal);
        var id = FirstClaimOrDefault(claims, IdClaimKeys) ?? httpUserIdentityName;
        var name = FirstClaimOrDefault(claims, NameClaimKeys) ?? httpUserIdentityName;
        var identity = new UserIdentity(schema, id);
        var identities = new ApiMap<UserIdentity, string>() {
            { identity, "" },
        };

        AccountFull account;
        if (existingAccount == null)
            // Create
            account = new AccountFull("") {
                Name = name,
                Claims = claims,
                Identities = identities,
            };
        else {
            // Update
            account = existingAccount with {
                Claims = claims.WithMany(existingAccount.Claims),
                Identities = identities,
            };
        }
        return (account, identity);
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
