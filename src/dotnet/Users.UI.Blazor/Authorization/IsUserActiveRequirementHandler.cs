using Microsoft.AspNetCore.Authorization;

namespace ActualChat.Users.UI.Blazor.Authorization;

public class IsUserActiveRequirementHandler : AuthorizationHandler<IsUserActiveRequirement>
{
    private readonly Session _session;
    private readonly IAuthz _authz;

    public IsUserActiveRequirementHandler(Session session, IAuthz authz)
    {
        _session = session;
        _authz = authz;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, IsUserActiveRequirement requirement)
    {
        if (await _authz.IsActive(_session, default).ConfigureAwait(false))
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
