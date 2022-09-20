using ActualChat.Hosting;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public abstract class NavbarContentBase : ComputedStateComponent<NavbarContentBase.Model>
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IAccounts Accounts { get; init; } = null!;
    [Inject] protected HostInfo HostInfo { get; init; } = null!;
    [Inject] protected IEnumerable<NavbarWidget> Widgets { get; init; } = null!;

    protected ImmutableArray<NavbarWidget> OrderedWidgets { get; private set; }

    protected override void OnInitialized()
    {
        OrderedWidgets = Widgets.OrderBy(w => w.Order).ToImmutableArray();
        base.OnInitialized();
    }

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() {
            InitialValue = Model.Guest,
            UpdateDelayer = UpdateDelayer.Instant,
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
