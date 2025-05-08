using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public abstract class AccountBadgeBase : ComputedStateComponent<AccountBadgeBase.Model>
{
    [Inject] private UIHub Hub { get; init; } = null!;

    private Session Session => Hub.Session();
    private IAccounts Accounts => Hub.Accounts;

    protected UserId? UserId { get; private set; }

    [Parameter, EditorRequired] public string UserSid { get; set; } = "";

    protected override void OnParametersSet()
        => UserId = UserId.Parse(UserSid);

    protected override ComputedState<Model>.Options GetStateOptions()
        => ComputedStateComponent.GetStateOptions(GetType(),
            static t => new ComputedState<Model>.Options() {
                InitialValue = Model.Loading,
                Category = GetStateCategory(t),
            });

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var userId = UserId;
        if (userId is null)
            return Model.None;

        var account = await Accounts.Get(Session, userId, cancellationToken).ConfigureAwait(false);
        return account == null
            ? Model.None
            : new(account);
    }

    // Nested types

    public sealed record Model(Account Account) {
        public static readonly Model None = new(SpecialAccount.None);
        public static readonly Model Loading = new(SpecialAccount.Loading);
    }
}
