namespace ActualChat.UI.Blazor.Components;

public abstract class AccountBadgeBase : ComputedStateComponent<UIHub, AccountBadgeBase.Model>
{
    private IAccounts Accounts => Hub.Accounts;

    [Parameter, EditorRequired] public UserId? UserId { get; set; }

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

    public sealed record Model(Account? Account = null) {
        public static readonly Model None = new();
        public static readonly Model Loading = new();

        // This record relies on referential equality
        public bool Equals(Model? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
}
