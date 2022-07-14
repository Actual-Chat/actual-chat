using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public abstract class UserAuthorBadgeBase : ComputedStateComponent<UserAuthorBadgeBase.Model>
{
    [Inject] private IAccounts Accounts { get; init; } = null!;
    [Inject] private IUserPresences UserPresences { get; init; } = null!;

    [Parameter, EditorRequired] public string UserId { get; set; } = "";
    [Parameter] public bool ShowsPresence { get; set; }

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() { InitialValue = Model.None };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        if (UserId.IsNullOrEmpty())
            return Model.None;

        var author = await Accounts.GetUserAuthor(UserId, cancellationToken).ConfigureAwait(true);
        if (author == null)
            return Model.None;

        if (string.IsNullOrWhiteSpace(author.Picture)) {
            var picture = $"https://avatars.dicebear.com/api/avataaars/{author.Name}.svg";
            author = author with {
                Picture = picture,
            };
        }

        var presence = Presence.Unknown;
        if (ShowsPresence)
            presence = await UserPresences.Get(UserId, cancellationToken).ConfigureAwait(true);

        return new(author, presence);
    }

    public sealed record Model(Author Author, Presence Presence = Presence.Unknown) {
        public static Model None { get; } = new(new Author());
    }
}
