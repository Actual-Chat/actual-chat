using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

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
        var userId = user.Claims
            .FirstOrDefault(c => string.Equals(c.Type, ClaimTypes.NameIdentifier))?.Value ?? "";
        if (userId.IsNullOrEmpty())
            return;
        if (await _userInfos.IsAdmin(userId, default).ConfigureAwait(false))
            context.Succeed(requirement);
    }
}
