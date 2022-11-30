using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.Server.Authentication;

namespace ActualChat.Users;

public class AppServerAuthHelper : ServerAuthHelper
{
    private readonly string _closeWindowAppRequestPath;
    private ClaimMapper ClaimMapper { get; }

    public AppServerAuthHelper(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        ClaimMapper = services.GetRequiredService<ClaimMapper>();
        _closeWindowAppRequestPath = Settings.CloseWindowRequestPath + "-app";
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

    protected override Task<SessionInfo> SetupSession(
        Session session,
        SessionInfo? sessionInfo,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var setupSessionCommand = new SetupSessionCommand(session, ipAddress, userAgent);
        if ((sessionInfo?.UserId ?? "").Length == 0) { // Unauthenticated
            var guestId = sessionInfo.GetGuestId();
            if (guestId.IsNone) // No GuestId
                setupSessionCommand = setupSessionCommand with {
                    Options = ImmutableOptionSet.Empty.Set(new GuestIdOption(UserId.NewGuest())),
                };
        }
        return Commander.Call(setupSessionCommand, true, cancellationToken);
    }

    protected override (User User, UserIdentity AuthenticatedIdentity) CreateOrUpdateUser(User? user, ClaimsPrincipal httpUser, string schema)
    {
        var (newUser, userIdentity) = base.CreateOrUpdateUser(user, httpUser, schema);
        var httpClaims = httpUser.Claims.ToDictionary(c => c.Type, c => c.Value, StringComparer.Ordinal);
        newUser = ClaimMapper.UpdateClaims(newUser, httpClaims);
        return (newUser, userIdentity);
    }
}
