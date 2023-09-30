using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public abstract class AccountBadgeBase : ComputedStateComponent<AccountBadgeBase.Model>
{
    [Inject] private Session Session { get; init; } = null!;
    [Inject] private IAccounts Accounts { get; init; } = null!;
    [Inject] private IUserPresences UserPresences { get; init; } = null!;

    protected UserId UserId { get; private set; }

    [Parameter, EditorRequired] public string UserSid { get; set; } = "";
    [Parameter] public bool ShowPresence { get; set; }

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

        var presence = Presence.Unknown;
        if (ShowPresence)
            presence = await UserPresences.Get(UserId, cancellationToken);

        return new(account, presence);
    }

    public sealed record Model(
        Account Account,
        Presence Presence = Presence.Unknown
    ) {
        public static readonly Model None = new(Account.None);
        public static readonly Model Loading = new(Account.Loading); // Should differ by ref. from None
    }
}
