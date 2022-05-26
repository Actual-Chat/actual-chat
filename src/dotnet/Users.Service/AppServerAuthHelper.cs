using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Stl.Fusion.Server.Authentication;
using Stl.Fusion.Server.Internal;

namespace ActualChat.Users;

public class AppServerAuthHelper : ServerAuthHelper
{
    private readonly string _closeWindowAppRequestPath;
    private ClaimMapper ClaimMapper { get; }

    public AppServerAuthHelper(
        Options? settings,
        IAuth auth,
        IAuthBackend authBackend,
        ISessionResolver sessionResolver,
        AuthSchemasCache authSchemasCache,
        ClaimMapper claimMapper,
        MomentClockSet clocks)
        : base(settings, auth, authBackend, sessionResolver, authSchemasCache, clocks)
    {
        ClaimMapper = claimMapper;
        _closeWindowAppRequestPath = Settings.CloseWindowRequestPath + "-app";
    }

    public override bool IsCloseWindowRequest(HttpContext httpContext, out string closeWindowFlowName)
    {
        var request = httpContext.Request;
        var isCloseWindowRequest = StringComparer.Ordinal.Equals(request.Path.Value, Settings.CloseWindowRequestPath)
            || StringComparer.Ordinal.Equals(request.Path.Value, _closeWindowAppRequestPath);
        closeWindowFlowName = "";
        if (isCloseWindowRequest && request.Query.TryGetValue("flow", out var flows))
            closeWindowFlowName = flows.FirstOrDefault() ?? "";
        return isCloseWindowRequest;
    }

    protected override (User User, UserIdentity AuthenticatedIdentity) UpsertUser(User user, ClaimsPrincipal httpUser, string schema)
    {
        var (newUser, userIdentity) = base.UpsertUser(user, httpUser, schema);
        var httpClaims = httpUser.Claims.ToDictionary(c => c.Type, c => c.Value, StringComparer.Ordinal);
        newUser = ClaimMapper.UpdateClaims(newUser, httpClaims);
        return (newUser, userIdentity);
    }
}
