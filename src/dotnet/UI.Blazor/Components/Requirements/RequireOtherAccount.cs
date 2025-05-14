using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public sealed class RequireOtherAccount : RequirementComponent
{
    private IAccounts Accounts => Hub.Accounts;

    [Parameter, EditorRequired] public string UserSid { get; set; } = "";
    [Parameter] public bool MustNotBeGuest { get; set; }

    public override string ToString()
        => $"{GetType().GetName()}(UserSid = {UserSid})";

    public override async Task Require(CancellationToken cancellationToken)
    {
        var userId = UserId.Parse(UserSid);
        var account = await Accounts.Get(Session, userId, cancellationToken).ConfigureAwait(false);
        account.Require(MustNotBeGuest ? Account.MustNotBeGuest : Account.MustExist);
    }
}
