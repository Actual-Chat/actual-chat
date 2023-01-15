using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public class RequireOtherAccount : RequirementComponent
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IAccounts Accounts { get; init; } = null!;

    [Parameter, EditorRequired] public string UserSid { get; set; } = "";
    [Parameter] public bool MustNotBeGuest { get; set; }

    public override async Task<Unit> Require(CancellationToken cancellationToken)
    {
        var userId = new UserId(UserSid);
        var account = await Accounts.Get(Session, userId, cancellationToken).ConfigureAwait(false);
        account.Require(MustNotBeGuest ? Account.MustNotBeGuest : Account.MustExist);
        return default;
    }
}
