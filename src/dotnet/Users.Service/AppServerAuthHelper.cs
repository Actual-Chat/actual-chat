using System.Security.Claims;
using System.Text;
using ActualChat.Hashing;
using Microsoft.AspNetCore.Http;
using ActualLab.Fusion.Server.Authentication;

namespace ActualChat.Users;

public class AppServerAuthHelper : ServerAuthHelper
{
    private readonly string _closeWindowAppRequestPath;
    private ClaimMapper ClaimMapper { get; }
    private IAccountsBackend AccountsBackend { get; }
    
    public AppServerAuthHelper(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        ClaimMapper = services.GetRequiredService<ClaimMapper>();
        _closeWindowAppRequestPath = Settings.CloseWindowRequestPath + "-app";
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
    }

    public override bool IsCloseWindowRequest(HttpContext httpContext, out string closeWindowFlowName)
    {
        var request = httpContext.Request;
        var isCloseWindowRequest = OrdinalEquals(request.Path.Value, Settings.CloseWindowRequestPath)
            || OrdinalEquals(request.Path.Value, _closeWindowAppRequestPath);
        closeWindowFlowName = "";
        if (isCloseWindowRequest && request.Query.TryGetValue("flow", out var flows))
            closeWindowFlowName = flows.FirstOrDefault() ?? "";
        return isCloseWindowRequest;
    }

    private async Task<(User User, UserIdentity AuthenticatedIdentity)> CreateOrUpdateUser(User? user, ClaimsPrincipal httpUser, string schema, CancellationToken cancellationToken)
    {
        using var _activity = AppDiagnostics.AppTrace.StartActivity("CreateOrUpdateUser");
        var (newUser, userIdentity) = base.CreateOrUpdateUser(user, httpUser, schema);
        var httpClaims = httpUser.Claims.ToDictionary(c => c.Type, c => c.Value, StringComparer.Ordinal);
        newUser = ClaimMapper.UpdateClaims(newUser, httpClaims);
        await UseExistingEmailIdentity().ConfigureAwait(false);
        return (newUser, userIdentity);

        async Task UseExistingEmailIdentity()
        {
            var existingUserId = await AccountsBackend.GetIdByUserIdentity(userIdentity, cancellationToken).ConfigureAwait(false);
            // Check if user with such email exists when logging in with external identity
            if (!existingUserId.IsNone || !Constants.Auth.IsExternalEmailScheme(schema) || httpUser.FindFirstValue(ClaimTypes.Email) is not { } email)
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

    protected override async Task SignIn(
        Session session,
        SessionInfo sessionInfo,
        User? user,
        ClaimsPrincipal httpUser,
        string httpAuthenticationSchema,
        CancellationToken cancellationToken)
    {
        using var _activity = AppDiagnostics.AppTrace.StartActivity("SignIn");
        
        var (newUser, authenticatedIdentity) =
            await CreateOrUpdateUser(user, httpUser, httpAuthenticationSchema, cancellationToken).ConfigureAwait(false);
        
        var signInCommand = new AuthBackend_SignIn(session, newUser, authenticatedIdentity);
        using (var _ = AppDiagnostics.AppTrace.StartActivity("SignIn:AuthBackend_SignIn")) {
            await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);
        }
    }
    public override async Task UpdateAuthState(Session session, HttpContext httpContext, bool assumeAllowed, CancellationToken cancellationToken = default) {
        using var _activity = AppDiagnostics.AppTrace.StartActivity("UpdateAuthState");
        await base.UpdateAuthState(session, httpContext, assumeAllowed, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task<SessionInfo?> GetSessionInfo(Session session, CancellationToken cancellationToken) {
        using var _activity = AppDiagnostics.AppTrace.StartActivity("GetSessionInfo");
        return await base.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
    }
    protected override async Task<SessionInfo> SetupSession(Session session, SessionInfo? sessionInfo, string ipAddress, string userAgent, CancellationToken cancellationToken) {
        using var _activity = AppDiagnostics.AppTrace.StartActivity("SetupSession");
        return await base.SetupSession(session, sessionInfo, ipAddress, userAgent, cancellationToken).ConfigureAwait(false);
    }
    protected override async Task<User?> GetUser(Session session, SessionInfo sessionInfo, CancellationToken cancellationToken) {
        using var _activity = AppDiagnostics.AppTrace.StartActivity("GetUser");
        return await base.GetUser(session, sessionInfo, cancellationToken).ConfigureAwait(false);
    }
    protected override async Task SignOut(Session session, SessionInfo sessionInfo, CancellationToken cancellationToken) {
        using var _activity = AppDiagnostics.AppTrace.StartActivity("SignOut");
        await base.SignOut(session, sessionInfo, cancellationToken).ConfigureAwait(false);
    }
    protected override async Task UpdatePresence(Session session, SessionInfo sessionInfo, CancellationToken cancellationToken) {
        using var _activity = AppDiagnostics.AppTrace.StartActivity("UpdatePresence");
        await base.UpdatePresence(session, sessionInfo, cancellationToken).ConfigureAwait(false);
    }
}
