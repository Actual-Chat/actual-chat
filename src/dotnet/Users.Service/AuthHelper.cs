using System.Security.Claims;
using ActualChat.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace ActualChat.Users;

public sealed class AuthHelper
{
    public string[] IdClaimKeys { get; init; } = [ClaimTypes.NameIdentifier];
    public string CloseFlowRequestPath { get; init; } = "/fusion/close";
    public string AppCloseFlowRequestPath { get; init; } = "/fusion/close-app";
    public TimeSpan SessionInfoUpdatePeriod { get; init; } = Constants.Session.LastSeenAtUpdatePeriod;

    public HostInfo HostInfo { get; }
    public IAccounts Accounts { get; }
    public IAccountsBackend AccountsBackend { get; }
    public ISessionsBackend SessionsBackend { get; }
    public ICommander Commander { get; }
    public MomentClockSet Clocks { get; }

    private ClaimMapper ClaimMapper { get; }
    private ILogger Log { get; }

    public AuthHelper(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();

        HostInfo = services.HostInfo();
        Accounts = services.GetRequiredService<IAccounts>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        SessionsBackend = services.GetRequiredService<ISessionsBackend>();
        ClaimMapper = services.GetRequiredService<ClaimMapper>();
        Commander = services.Commander();
    }

    public async Task<AuthState> UpdateAuthState(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var signInError = (string?)null;
        var isCloseFlow = IsCloseFlow(httpContext, out var closeFlow);
        var isAnyAuthFlow = isCloseFlow
            || IsSignInFlow(httpContext)
            || IsSignOutFlow(httpContext);

        // Path A: Token-based session (MAUI sign-in/sign-out/close flows)
        // Token-based sessions are mobile app sessions — they must NEVER leak to browser's session cookie.
        // The session token cookie is set by MauiAuthController.Start and removed on close flow.
        var tokenSession = isCloseFlow
            ? httpContext.TryPullSessionFromTokenCookie()
            : null;
        if (tokenSession != null) {
            if (!await Accounts.IsValidSession(tokenSession, cancellationToken).ConfigureAwait(false))
                throw StandardError.Unauthorized("Your session is expired or deactivated. Please restart the app.");
            try {
                await UpdateAuthState(tokenSession, httpContext, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                signInError = e.Message;
                var setErrorCmd = new SessionTemporalsBackend_Set(
                    tokenSession, Constants.SessionTemporals.SignInErrorKey, signInError);
                await Commander.Run(setErrorCmd, true, cancellationToken).ConfigureAwait(false);
            }
            return AuthState.New(tokenSession, isAnyAuthFlow, closeFlow, signInError);
        }

        // Path B: Cookie-based session (normal web flow)
        var cookieSession = httpContext.TryGetSessionFromCookie();
        if (!await Accounts.IsValidSession(cookieSession, cancellationToken).ConfigureAwait(false))
            cookieSession = null;
        var session = cookieSession ?? Session.New();
        for (var tryIndex = 0;; tryIndex++) {
            try {
                await UpdateAuthState(session, httpContext, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                if (tryIndex < 1 && e is InvalidOperationException && e.Message.Contains("Inactive session")) {
                    session = Session.New();
                    continue;
                }
                signInError = e.Message;
                if (session == cookieSession) { // Otherwise, no one is aware of a new session
                    var setErrorCmd = new SessionTemporalsBackend_Set(
                        session, Constants.SessionTemporals.SignInErrorKey, signInError);
                    await Commander.Run(setErrorCmd, true, cancellationToken).ConfigureAwait(false);
                }
                break;
            }
        }

        // Handle new session: set cookie, delete render mode cookie
        if (session != cookieSession) {
            httpContext.AddSessionCookie(session);
            // httpContext.Response.Cookies.Delete(RenderModeEndpoint.Cookie.Name!);
        }
        return AuthState.New(session, isAnyAuthFlow, closeFlow, signInError);
    }

    public async Task SignIn(
        Session session,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (session.Kind is SessionKind.ApiKey)
            throw StandardError.Unavailable("Cannot use API key session here.");

        var authSchema = principal.Identity?.AuthenticationType ?? "";
        if (authSchema.IsNullOrEmpty())
            throw StandardError.Constraint("ClaimsPrincipal has no authentication type.");

        // No HttpContext here — register the session if it's missing, but don't churn IP/UA.
        var sessionInfo = await Accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo == null) {
            var upsertSessionCmd = new SessionsBackend_Upsert(session);
            await Commander.Call(upsertSessionCmd, true, cancellationToken).ConfigureAwait(false);
        }

        var existingAccount = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var isSignedIn = !existingAccount.IsGuest;
        if (isSignedIn && IsSameAccount(existingAccount, principal, authSchema))
            return;

        var signInCommand = await BuildSignInCommand(session, principal, authSchema, cancellationToken)
            .ConfigureAwait(false);
        await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAuthState(
        Session session,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (session.Kind is SessionKind.ApiKey)
            throw StandardError.Unavailable("Cannot use API key session here.");

        var httpUser = httpContext.User;
        var authSchema = httpUser.Identity?.AuthenticationType ?? "";
        var httpIsSignedIn = !authSchema.IsNullOrEmpty();

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

        if (!IsCloseFlow(httpContext))
            return; // Actual SignIn/SignOut actions are performed on close flow only

        if (httpIsSignedIn) {
            if (isSignedIn && IsSameAccount(existingAccount, httpUser, authSchema))
                return; // Nothing to change

            var signInCommand = await BuildSignInCommand(session, httpUser, authSchema, cancellationToken)
                .ConfigureAwait(false);
            await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);
        }
        else if (isSignedIn) {
            var signOutCommand = new AccountsBackend_SignOut(session);
            await ((Task)Commander.Call(signOutCommand, true, cancellationToken)).ConfigureAwait(false);
        }
    }

    // Private methods

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

    private async Task<AccountsBackend_SignIn> BuildSignInCommand(
        Session session,
        ClaimsPrincipal httpUser,
        string authSchema,
        CancellationToken cancellationToken)
    {
        var httpUserIdentityName = httpUser.Identity?.Name ?? "";
        var claims = httpUser.Claims.ToApiMap(c => c.Type, c => c.Value);
        var id = FirstClaimOrDefault(claims, IdClaimKeys) ?? httpUserIdentityName;
        var identity = new UserIdentity(authSchema, id);
        var identities = new ApiMap<UserIdentity, string>();

        // Map claims using ClaimMapper
        var httpClaims = httpUser.Claims.ToDictionary(c => c.Type, c => c.Value);
        (claims, _) = ClaimMapper.UpdateClaims(claims, httpClaims);

        // For external providers, try to link by email if the identity doesn't exist yet
        var authenticatedIdentity = identity;
        if (!AuthSchema.IsExternal(authSchema))
            goto exit;

        var existingUserId = await AccountsBackend
            .GetIdByUserIdentity(identity, cancellationToken)
            .ConfigureAwait(false);
        if (existingUserId is not null
            || !AuthSchema.HasVerifiedEmail(claims)
            || claims.GetValueOrDefault(ClaimTypes.Email) is not { } emailClaim
            || !ActualChat.Email.TryParse(emailClaim, out var email))
            goto exit;

        // Provider identity is not found - try to find existing user by email identity
        var emailIdentity = UserIdentityExt.NewEmailIdentity(email);
        var userId = await AccountsBackend.GetIdByUserIdentity(emailIdentity, cancellationToken).ConfigureAwait(false)
            ?? await AccountsBackend.GetUserIdByEmailHash(email.Hash, cancellationToken).ConfigureAwait(false);
        if (userId is null)
            goto exit;

        // Found existing user by email or email hash - keep provider as authenticatedIdentity,
        // add email identity so OnSignIn can find the user via fallback lookup
        identities = identities.WithEmailIdentity(email);

        exit:
        return new AccountsBackend_SignIn(session, authenticatedIdentity, identities, claims);
    }

    private static string? FirstClaimOrDefault(IReadOnlyDictionary<string, string> claims, string[] keys)
    {
        foreach (var key in keys)
            if (claims.TryGetValue(key, out var value) && !value.IsNullOrEmpty())
                return value;
        return null;
    }

    // AllowXxx

    private static bool IsSignInFlow(HttpContext httpContext)
        => httpContext.Request.Path.Value?.StartsWith("/signIn", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsSignOutFlow(HttpContext httpContext)
        => httpContext.Request.Path.Value?.StartsWith("/signOut", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsCloseFlow(HttpContext httpContext)
        => IsCloseFlow(httpContext, out _);
    private bool IsCloseFlow(HttpContext httpContext, out CloseFlow? closeFlowInfo)
    {
        closeFlowInfo = null;
        var request = httpContext.Request;
        if (request.Path.Value != CloseFlowRequestPath
            && request.Path.Value != AppCloseFlowRequestPath)
            return false;

        var name = "";
        if (request.Query.TryGetValue("flow", out var flowValues))
            name = (flowValues.FirstOrDefault() ?? "").Capitalize();
        if (name.IsNullOrEmpty())
            return false;

        string? redirectUrl = null;
        if (request.Query.TryGetValue("redirectUrl", out var returnUrlValues))
            redirectUrl = AuthRedirectUrl.Sanitize(returnUrlValues.FirstOrDefault());

        var mustClose = true;
        if (request.Query.TryGetValue("mustClose", out var mustCloseValues))
            mustClose = int.TryParse(mustCloseValues.FirstOrDefault(), out var x) && x != 0;
        closeFlowInfo = new CloseFlow(name, redirectUrl, mustClose);
        return true;
    }

    // Nested types

    public record AuthState(
        Session Session,
        bool IsAnyAuthFlow,
        CloseFlow? CloseFlow)
    {
        public static AuthState New(
            Session session, bool isAnyAuthFlow, CloseFlow? closeFlow,
            string? error = null)
        {
            if (error is null)
                return new AuthState(session, isAnyAuthFlow, closeFlow);
            if (closeFlow is not null)
                return new AuthState(session, isAnyAuthFlow, closeFlow with { Error = error });

            closeFlow = new CloseFlow("Sign-in", null, true, error);
            return new AuthState(session, isAnyAuthFlow, closeFlow);
        }
    }

    public sealed record CloseFlow(
        string Name,
        string? RedirectUrl,
        bool MustClose,
        string? Error = null);
}
