using Microsoft.AspNetCore.Authorization;

namespace ActualChat.Users.UI.Blazor.Authorization;

public class IsUserActiveRequirementHandler : AuthorizationHandler<IsUserActiveRequirement>
{
    private readonly Session _session;
    private readonly IUserProfiles _userProfiles;

    public IsUserActiveRequirementHandler(Session session, IUserProfiles userProfiles)
    {
        _session = session;
        _userProfiles = userProfiles;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, IsUserActiveRequirement requirement)
    {
        if (await _userProfiles.IsActive(_session, default).ConfigureAwait(false))
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
