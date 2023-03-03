using System.Security;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public class RequireAccount : RequirementComponent
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IAccounts Accounts { get; init; } = null!;

    [Parameter] public bool MustBeActive { get; set; } = true;
    [Parameter] public bool MustBeAdmin { get; set; }

    public override string ToString()
        => $"{GetType().GetName()}(MustBeActive = {MustBeActive}, MustBeAdmin = {MustBeAdmin})";

    public override async Task<Unit> Require(CancellationToken cancellationToken)
    {
        // Caching all used properties to use ConfigureAwait(false) here
        var mustBeActive = MustBeActive;
        var mustBeAdmin = MustBeAdmin;
        var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        if (mustBeAdmin) {
            account.Require(AccountFull.MustBeAdmin);
            return default; // No extra checks are needed in this case
        }
        if (mustBeActive)
            account.Require(AccountFull.MustBeActive);
        return default;
    }
}
