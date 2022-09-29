using ActualChat.Hosting;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public abstract class NavbarContentBase : ComputedStateComponent<NavbarContentBase.Model>
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IAccounts Accounts { get; init; } = null!;
    [Inject] protected HostInfo HostInfo { get; init; } = null!;

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() {
            InitialValue = Model.Guest,
            UpdateDelayer = FixedDelayer.Instant,
        };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var account = await Accounts.Get(Session, cancellationToken).ConfigureAwait(false);
        return account == null ? Model.Guest : new Model(account);
    }

    public record Model(Account Account) {
        public static Model Guest { get; } = new(Account.Guest);

        public User User => Account.User;
    }
}
