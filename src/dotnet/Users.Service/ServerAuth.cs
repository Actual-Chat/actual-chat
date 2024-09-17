using System.Security.Claims;
using System.Text;
using ActualChat.Hashing;
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

    private HostInfo HostInfo { get; }
    private IAuth Auth { get; }
    private IAccountsBackend AccountsBackend { get; }
    private ClaimMapper ClaimMapper { get; }
    private ICommander Commander { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public ServerAuth(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();

        HostInfo = services.HostInfo();
        Auth = services.GetRequiredService<IAuth>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        ClaimMapper = services.GetRequiredService<ClaimMapper>();
        Commander = services.Commander();

        if (HostInfo.IsDevelopmentInstance)
            AllowSignIn = AllowAnywhere;
    }

    public bool IsCloseFlow(HttpContext httpContext, out string flowName, out string redirectUrl, out bool mustClose)
    {
        var request = httpContext.Request;
        var result =
            OrdinalEquals(request.Path.Value, CloseFlowRequestPath)
            || OrdinalEquals(request.Path.Value, AppCloseFlowRequestPath);
        flowName = "";
        if (result && request.Query.TryGetValue("flow", out var flowValues))
            flowName = (flowValues.FirstOrDefault() ?? "").Capitalize();
        redirectUrl = "";
        if (result && request.Query.TryGetValue("redirectUrl", out var returnUrlValues))
            redirectUrl = returnUrlValues.FirstOrDefault() ?? "";
        mustClose = true;
        if (result && request.Query.TryGetValue("mustClose", out var mustCloseValues))
            mustClose = int.TryParse(mustCloseValues.FirstOrDefault(), CultureInfo.InvariantCulture, out var x) && x != 0;
        return result;
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
        Session session,
        HttpContext httpContext,
        bool assumeAllowed,
        CancellationToken cancellationToken)
    {
        var httpUser = httpContext.User;
        var httpAuthenticationSchema = httpUser.Identity?.AuthenticationType ?? "";
        var httpIsSignedIn = !httpAuthenticationSchema.IsNullOrEmpty();

        var ipAddress = httpContext.GetRemoteIPAddress()?.ToString() ?? "";
        var userAgent = httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgentValues)
            ? userAgentValues.FirstOrDefault() ?? ""
            : "";

        var sessionInfo = await GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var mustSetupSession =
            sessionInfo == null
            || !OrdinalEquals(sessionInfo.IPAddress, ipAddress)
            || !OrdinalEquals(sessionInfo.UserAgent, userAgent)
            || sessionInfo.LastSeenAt + SessionInfoUpdatePeriod < Clocks.SystemClock.Now;
        if (mustSetupSession || sessionInfo == null)
            sessionInfo = await SetupSession(session, sessionInfo, ipAddress, userAgent, cancellationToken)
                .ConfigureAwait(false);

        var user = await GetUser(session, sessionInfo, cancellationToken).ConfigureAwait(false);
        var isSignedIn = IsSignedIn(user);
        try {
            if (httpIsSignedIn) {
                if (isSignedIn && IsSameUser(user, httpUser, httpAuthenticationSchema))
                    return; // Nothing to change

                var isSignInAllowed = !isSignedIn
                    ? assumeAllowed || AllowSignIn(this, httpContext)
                    : assumeAllowed || AllowChange(this, httpContext);
                if (!isSignInAllowed)
                    return; // Sign-in or user change is not allowed for the current location

                await SignIn(session, sessionInfo, user, httpUser, httpAuthenticationSchema, cancellationToken).ConfigureAwait(false);
            }
            else if (isSignedIn && (assumeAllowed || AllowSignOut(this, httpContext)))
                await SignOut(session, sessionInfo, cancellationToken).ConfigureAwait(false);
        }
        finally {
            // This should be done once important things are completed
            await UpdatePresence(session, sessionInfo, cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private Task<SessionInfo?> GetSessionInfo(Session session, CancellationToken cancellationToken)
        => Auth.GetSessionInfo(session, cancellationToken);

    private Task<User?> GetUser(
        Session session, SessionInfo sessionInfo,
        CancellationToken cancellationToken)
        => Auth.GetUser(session, cancellationToken);

    private Task<SessionInfo> SetupSession(
        Session session, SessionInfo? sessionInfo, string ipAddress, string userAgent,
        CancellationToken cancellationToken)
    {
        var setupSessionCommand = new AuthBackend_SetupSession(session, ipAddress, userAgent);
        return Commander.Call(setupSessionCommand, true, cancellationToken);
    }

    private async Task SignIn(
        Session session,
        SessionInfo sessionInfo,
        User? user,
        ClaimsPrincipal httpUser,
        string httpAuthenticationSchema,
        CancellationToken cancellationToken)
    {
        var (newUser, authenticatedIdentity) =
            await CreateOrUpdateUser(user, httpUser, httpAuthenticationSchema, cancellationToken).ConfigureAwait(false);
        var signInCommand = new AuthBackend_SignIn(session, newUser, authenticatedIdentity);
        await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private Task SignOut(
        Session session, SessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        var signOutCommand = new Auth_SignOut(session);
        return Commander.Call(signOutCommand, true, cancellationToken);
    }

    private Task UpdatePresence(
        Session session, SessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        _ = Auth.UpdatePresence(session, CancellationToken.None);
        return Task.CompletedTask;
    }

    private static bool IsSignedIn(User? user)
        => user?.IsAuthenticated() == true;

    private bool IsSameUser(User? user, ClaimsPrincipal httpUser, string schema)
    {
        if (user == null)
            return false;

        var httpUserIdentityName = httpUser.Identity?.Name ?? "";
        var claims = httpUser.Claims.ToImmutableDictionary(c => c.Type, c => c.Value);
        var id = FirstClaimOrDefault(claims, IdClaimKeys) ?? httpUserIdentityName;
        var identity = new UserIdentity(schema, id);
        return user.Identities.ContainsKey(identity);
    }

    private async Task<(User User, UserIdentity AuthenticatedIdentity)> CreateOrUpdateUser(User? user, ClaimsPrincipal httpUser, string schema, CancellationToken cancellationToken)
    {
        var (newUser, userIdentity) = BaseCreateOrUpdateUser(user, httpUser, schema);
        var httpClaims = httpUser.Claims.ToDictionary(c => c.Type, c => c.Value, StringComparer.Ordinal);
        newUser = ClaimMapper.UpdateClaims(newUser, httpClaims);
        await UseExistingEmailIdentity().ConfigureAwait(false);
        return (newUser, userIdentity);

        async Task UseExistingEmailIdentity()
        {
            var existingUserId = await AccountsBackend.GetIdByUserIdentity(userIdentity, cancellationToken).ConfigureAwait(false);
            // Check if user with such email exists when logging in with external identity
            if (!existingUserId.IsNone || !AuthSchema.IsExternal(schema) || httpUser.FindFirstValue(ClaimTypes.Email) is not { } email)
                return;

            var emailHash = email.Hash(Encoding.UTF8).SHA256().Base64();
            var userId = await AccountsBackend.GetIdByEmailHash(emailHash, cancellationToken)
                .ConfigureAwait(false);
            if (userId.IsNone)
                return;

            newUser = newUser.WithEmailIdentities(email);
            userIdentity = newUser.GetEmailIdentity();
        }
    }

    private (User User, UserIdentity AuthenticatedIdentity) BaseCreateOrUpdateUser(
        User? user, ClaimsPrincipal httpUser, string schema)
    {
        var httpUserIdentityName = httpUser.Identity?.Name ?? "";
        var claims = httpUser.Claims.ToApiMap(c => c.Type, c => c.Value, StringComparer.Ordinal);
        var id = FirstClaimOrDefault(claims, IdClaimKeys) ?? httpUserIdentityName;
        var name = FirstClaimOrDefault(claims, NameClaimKeys) ?? httpUserIdentityName;
        var identity = new UserIdentity(schema, id);
        var identities = new ApiMap<UserIdentity, string>() {
            { identity, "" },
        };

        if (user == null)
            // Create
            user = new User(Symbol.Empty, name) {
                Claims = claims,
                Identities = identities,
            };
        else {
            // Update
            user = user with {
                Claims = claims.WithMany(user.Claims),
                Identities = identities,
            };
        }
        return (user, identity);
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
        => h.IsCloseFlow(httpContext, out _, out _, out _);
}
