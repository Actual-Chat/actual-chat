using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public abstract class OwnAccountComponentBase : ComputedStateComponent<AccountFull>
{
    [Inject] protected AccountUI AccountUI { get; init; } = null!;
    [Inject] protected HostInfo HostInfo { get; init; } = null!;

    protected override ComputedState<AccountFull>.Options GetStateOptions()
        => new() {
            InitialValue = AccountUI.OwnAccount.Value,
            Category = GetStateCategory(),
        };

    protected override Task<AccountFull> ComputeState(CancellationToken cancellationToken)
        => AccountUI.OwnAccount.Use(cancellationToken).AsTask();
}
