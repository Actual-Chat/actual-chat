using ActualChat.Hosting;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public abstract class NavbarContentBase : ComputedStateComponent<AccountFull>
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IAccounts Accounts { get; init; } = null!;
    [Inject] protected HostInfo HostInfo { get; init; } = null!;

    protected override ComputedState<AccountFull>.Options GetStateOptions()
        => new() {
            InitialValue = AccountFull.Loading,
            UpdateDelayer = FixedDelayer.Instant,
        };

    protected override async Task<AccountFull> ComputeState(CancellationToken cancellationToken) {
        var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        return account ?? AccountFull.None;
    }
}
