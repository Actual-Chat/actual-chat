using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public abstract class AccountBadgeBase : ComputedStateComponent<AccountBadgeBase.Model>
{
    [Inject] private Session Session { get; init; } = null!;
    [Inject] private IAccounts Accounts { get; init; } = null!;

    protected UserId UserId { get; private set; }

    [Parameter, EditorRequired] public string UserSid { get; set; } = "";

    protected override void OnParametersSet()
        => UserId = new UserId(UserSid);

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() {
            InitialValue = Model.Loading,
            Category = GetStateCategory(),
        };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        if (UserId.IsNone)
            return Model.None;

        var account = await Accounts.Get(Session, UserId, cancellationToken);
        if (account == null)
            return Model.None;

        return new(account);
    }

    public record struct Model(Account Account) {
        public static readonly Model None = new(Account.None);
        public static readonly Model Loading = new(Account.Loading); // Should differ by ref. from None
    }
}
