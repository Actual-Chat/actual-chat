using System.Security.Claims;
using Stl.Fusion.Server.Internal;

namespace ActualChat.Users;

public class ServerAuthHelper : Stl.Fusion.Server.Authentication.ServerAuthHelper
{
    private ClaimMapper ClaimMapper { get; }

    public ServerAuthHelper(
        Options? settings,
        IAuth auth,
        IAuthBackend authBackend,
        ISessionResolver sessionResolver,
        AuthSchemasCache authSchemasCache,
        ClaimMapper claimMapper,
        MomentClockSet clocks)
        : base(settings, auth, authBackend, sessionResolver, authSchemasCache, clocks)
        => ClaimMapper = claimMapper;

    protected override (User User, UserIdentity AuthenticatedIdentity) UpsertUser(User user, ClaimsPrincipal httpUser, string schema)
    {
        var (newUser, userIdentity) = base.UpsertUser(user, httpUser, schema);
        var httpClaims = httpUser.Claims.ToDictionary(c => c.Type, c => c.Value, StringComparer.Ordinal);
        newUser = ClaimMapper.UpdateClaims(newUser, httpClaims);
        return (newUser, userIdentity);
    }
}
