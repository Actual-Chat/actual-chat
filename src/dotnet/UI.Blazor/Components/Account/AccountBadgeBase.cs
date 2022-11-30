using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public abstract class AccountBadgeBase : ComputedStateComponent<AccountBadgeBase.Model>
{
    [Inject] private Session Session { get; init; } = null!;
    [Inject] private IAccounts Accounts { get; init; } = null!;
    [Inject] private IUserPresences UserPresences { get; init; } = null!;

    [Parameter, EditorRequired] public string UserId { get; set; } = "";
    [Parameter] public bool ShowPresence { get; set; }

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() { InitialValue = Model.Loading };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var userId = new UserId(UserId, ParseOptions.OrNone);
        if (userId.IsNone)
            return Model.None;

        var account = await Accounts.Get(Session, userId, cancellationToken);
        if (account == null)
            return Model.None;

        var presence = Presence.Unknown;
        if (ShowPresence)
            presence = await UserPresences.Get(userId, cancellationToken);

        return new(account, presence);
    }

    public sealed record Model(
        Account Account,
        Presence Presence = Presence.Unknown
    ) {
        public static Model None { get; } = new(Account.None);
        public static Model Loading { get; } = new(Account.Loading); // Should differ by ref. from None
    }
}
