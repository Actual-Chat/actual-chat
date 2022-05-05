using Microsoft.AspNetCore.Authorization;

namespace ActualChat.Users.UI.Blazor.Authorization;

public class IsAdminRequirementHandler : AuthorizationHandler<IsAdminRequirement>
{
    private readonly Session _session;
    private readonly IUserProfiles _userProfiles;

    public IsAdminRequirementHandler(Session session, IUserProfiles userProfiles)
    {
        _session = session;
        _userProfiles = userProfiles;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, IsAdminRequirement requirement)
    {
        var userProfile = await _userProfiles.Get(_session, default).ConfigureAwait(false);
        if (userProfile?.IsAdmin == true)
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
