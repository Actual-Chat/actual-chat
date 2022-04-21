using Microsoft.AspNetCore.Authorization;

namespace ActualChat.Users.UI.Blazor.Authorization;

public class IsUserActiveRequirementHandler : AuthorizationHandler<IsUserActiveRequirement>
{
    private readonly Session _session;
    private readonly IAuthz _authorization;

    public IsUserActiveRequirementHandler(Session session, IAuthz authorization)
    {
        _session = session;
        _authorization = authorization;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, IsUserActiveRequirement requirement)
    {
        if (await _authorization.IsActive(_session, default).ConfigureAwait(false))
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
