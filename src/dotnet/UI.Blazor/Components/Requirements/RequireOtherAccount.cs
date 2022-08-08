using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public class RequireOtherAccount : RequirementComponent
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IAccounts Accounts { get; init; } = null!;

    [Parameter] public string UserId { get; set; } = "";

    public override async Task<Unit> Require(CancellationToken cancellationToken)
    {
        var chat = await Accounts.GetByUserId(Session, UserId, cancellationToken).ConfigureAwait(false);
        chat.Require(Account.MustBeAvailable);
        return default;
    }
}
