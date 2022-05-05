using ActualChat.Hosting;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public abstract class NavbarContentBase : ComputedStateComponent<NavbarContentBase.Model>
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IUserProfiles UserProfiles { get; init; } = null!;
    [Inject] protected HostInfo HostInfo { get; init; } = null!;
    [Inject] protected IEnumerable<NavbarWidget> Widgets { get; init; } = null!;

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() {
            InitialValue = Model.Guest,
            UpdateDelayer = UpdateDelayer.MinDelay,
        };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var userProfile = await UserProfiles.Get(Session, cancellationToken).ConfigureAwait(false);
        return userProfile == null ? Model.Guest : new Model(userProfile);
    }

    public record Model(UserProfile UserProfile) {
        public static Model Guest { get; } = new(UserProfile.Guest);
        public User User => UserProfile.User;
    }
}

