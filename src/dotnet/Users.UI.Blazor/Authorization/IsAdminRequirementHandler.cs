using Microsoft.AspNetCore.Authorization;

namespace ActualChat.Users.UI.Blazor.Authorization;

public class IsAdminRequirementHandler : AuthorizationHandler<IsAdminRequirement>
{
    private readonly Session _session;
    private readonly IAccounts _accounts;

    public IsAdminRequirementHandler(Session session, IAccounts accounts)
    {
        _session = session;
        _accounts = accounts;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, IsAdminRequirement requirement)
    {
        var account = await _accounts.Get(_session, default).ConfigureAwait(false);
        if (account is { IsAdmin: true })
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
