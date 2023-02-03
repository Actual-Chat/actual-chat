using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public abstract class NavbarContentBase : ComputedStateComponent<AccountFull>
{
    [Inject] protected AccountUI AccountUI { get; init; } = null!;
    [Inject] protected HostInfo HostInfo { get; init; } = null!;

    protected override ComputedState<AccountFull>.Options GetStateOptions()
        => new() {
            InitialValue = AccountUI.OwnAccount.Value,
            Category = GetStateCategory(),
        };

    protected override async Task<AccountFull> ComputeState(CancellationToken cancellationToken)
        => await AccountUI.OwnAccount.Use(cancellationToken);
}
