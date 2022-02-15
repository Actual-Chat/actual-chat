using Microsoft.AspNetCore.Authorization;

namespace ActualChat.Users.UI.Blazor;

internal class IsAdminAuthorizationPolicyHandler : AuthorizationHandler<IsAdminAuthorizationPolicyRequirement>
{
    private readonly IUserInfos _userInfos;

    public IsAdminAuthorizationPolicyHandler(IUserInfos userInfos)
        => _userInfos = userInfos;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, IsAdminAuthorizationPolicyRequirement requirement)
    {
        var user = context.User;
        if (!user.Identity?.IsAuthenticated ?? false)
            return;
        var userId = user.Claims.FirstOrDefault(c => c.Type==System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        if (userId.IsNullOrEmpty())
            return;
        if (await _userInfos.IsAdmin(userId, default))
            context.Succeed(requirement);
    }
}
