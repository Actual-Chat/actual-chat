using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public class RequireUserAuthor : RequirementComponent
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IAccounts Accounts { get; init; } = null!;

    [Parameter] public string UserId { get; set; } = "";

    public override async Task<Unit> Require(CancellationToken cancellationToken)
    {
        var userAuthor = await Accounts.GetUserAuthor(UserId, cancellationToken).ConfigureAwait(false);
        userAuthor.Require();
        return default;
    }
}
