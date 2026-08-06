
namespace ActualChat.UI.Blazor.App.Components;

public abstract class OwnAccountComponentBase<THub> : ComputedStateComponent<THub, AccountFull>
    where THub : UIHub
{
    protected override ComputedState<AccountFull>.Options GetStateOptions()
        => new() {
            InitialValue = AccountUI.OwnAccount.Value,
            Category = GetStateCategory(GetType()),
        };

    protected override Task<AccountFull> ComputeState(CancellationToken cancellationToken)
        => AccountUI.OwnAccount.Use(cancellationToken);
}
