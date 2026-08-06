namespace ActualChat.UI.Blazor.Components;

public sealed class RequireAccount : RequirementComponent
{
    private IAccounts Accounts => Hub.Accounts;

    [Parameter] public bool MustBeActive { get; set; } = true;
    [Parameter] public bool MustBeAdmin { get; set; }

    public override string ToString()
        => $"{GetType().GetName()}(MustBeActive = {MustBeActive}, MustBeAdmin = {MustBeAdmin})";

    public override async Task Require(CancellationToken cancellationToken)
    {
        // Caching all used properties to use ConfigureAwait(false) here
        var mustBeActive = MustBeActive;
        var mustBeAdmin = MustBeAdmin;

        var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        if (mustBeAdmin) {
            account.Require(AccountFull.MustBeAdmin);
            return; // No extra checks are needed in this case
        }
        if (mustBeActive)
            account.Require(AccountFull.MustBeActive);
    }
}
