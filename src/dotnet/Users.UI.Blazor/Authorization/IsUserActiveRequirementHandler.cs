using Microsoft.AspNetCore.Authorization;

namespace ActualChat.Users.UI.Blazor.Authorization;

public class IsUserActiveRequirementHandler : AuthorizationHandler<IsUserActiveRequirement>
{
    private readonly Session _session;
    private readonly IAccounts _accounts;

    public IsUserActiveRequirementHandler(Session session, IAccounts accounts)
    {
        _session = session;
        _accounts = accounts;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, IsUserActiveRequirement requirement)
    {
        var account = await _accounts.Get(_session, default).ConfigureAwait(false);
        if (account.IsActive())
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
