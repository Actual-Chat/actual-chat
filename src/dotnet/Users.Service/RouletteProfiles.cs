using ActualChat.Kvas;
using ActualChat.Roulette;

namespace ActualChat.Users;

public class RouletteProfiles(IServiceProvider services) : IRouletteProfiles
{
    private const string SelectedRouletteProfileIdKvasKey = "SelectedRouletteProfileId";

    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAvatars Avatars { get; } = services.GetRequiredService<IAvatars>();
    private IRouletteProfilesBackend Backend { get; } = services.GetRequiredService<IRouletteProfilesBackend>();
    private IServerKvas ServerKvas { get; } = services.ServerKvas();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<Symbol> GetSelectedProfileId(Session session, CancellationToken cancellationToken)
    {
        var kvas = ServerKvas.GetClient(session);
        var selectedProfileId = await kvas.Get(SelectedRouletteProfileIdKvasKey, Symbol.Empty, cancellationToken).ConfigureAwait(false);
        if (selectedProfileId.IsEmpty)
            return Symbol.Empty;

        var profile = await GetOwnProfile(session, selectedProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
            return Symbol.Empty;

        return selectedProfileId;
    }

    // [ComputeMethod]
    public virtual async Task<Profile?> GetOwnProfile(
        Session session,
        Symbol profileId,
        CancellationToken cancellationToken)
    {
        if (profileId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(profileId));

        var profile = await Backend.GetProfile(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
            return null;

        var ownAccount = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (profile.UserId != ownAccount.Id)
            return null;

        return profile.ToProfile();
    }

    // Commands

    // [CommandHandler]
    public virtual async Task<Profile> OnChange(RouletteProfiles_UpsertProfile command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!; // It just spawns other commands, so nothing to do here

        if (command.Change.Kind == ChangeKind.Remove)
            throw StandardError.NotSupported("Remove change is not supported.");

        var session = command.Session;
        var profileId = command.ProfileId;
        if (profileId.IsEmpty) {
            if (command.Change.Kind != ChangeKind.Create)
                throw StandardError.Constraint("Invalid change kind.");

            var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
            var profile = command.Change.Create.Value;

            var avatar = new AvatarFull(account.Id).WithMissingPropertiesFrom(profile.Avatar);
            var avatarsChange = new AvatarsBackend_Change(Symbol.Empty, null, Change.Create(avatar));
            avatar = await Commander.Call(avatarsChange, cancellationToken).ConfigureAwait(false);
            profileId = avatar.Id;

            var prefsChange = new RouletteProfilesBackend_ChangePrefs(profileId, null, Change.Create(profile.Preferences));
            var preferences = await Commander.Call(prefsChange, cancellationToken).ConfigureAwait(false);

            var kvas = ServerKvas.GetClient(session);
            await ActualChat.Users.Avatars.UpdateAvatarList(kvas, avatarsChange.Change, avatar.Id).ConfigureAwait(false);

            return new Profile(profileId) {
                Avatar = avatar.ToAvatar(),
                Preferences = preferences
            };
        }
        else {
            var existentAvatar = await Avatars.GetOwn(session, profileId, cancellationToken).ConfigureAwait(false);
            if (existentAvatar is null)
                throw StandardError.Constraint("Invalid profile id.");

            var profile = command.Change.Update.Value;
            var avatar = new AvatarFull(existentAvatar.UserId, existentAvatar.Id).WithMissingPropertiesFrom(profile.Avatar);
            var avatarsChange = new Avatars_Change(session, profileId, profile.Avatar.Version, Change.Update(avatar));
            avatar = await Commander.Call(avatarsChange, cancellationToken).ConfigureAwait(false);

            var preferences = profile.Preferences;
            var prefsChange = preferences.IsStored()
                ? new RouletteProfilesBackend_ChangePrefs(profileId, null, Change.Update(preferences))
                : new RouletteProfilesBackend_ChangePrefs(profileId, null, Change.Create(preferences));
            preferences = await Commander.Call(prefsChange, cancellationToken).ConfigureAwait(false);

            return Profile.Create(avatar, preferences);
        }
    }

    // [CommandHandler]
    public virtual async Task OnSelectProfile(RouletteProfiles_SelectProfile command, CancellationToken cancellationToken)
    {
        var (session, profileId) = command;

        if (!profileId.IsEmpty) {
            var avatar = await Avatars.GetOwn(session, profileId, cancellationToken).ConfigureAwait(false);
            if (avatar is null)
                throw StandardError.Constraint("Invalid profile id.");
        }

        var kvas = ServerKvas.GetClient(session);
        await kvas.Set(SelectedRouletteProfileIdKvasKey, profileId, cancellationToken).ConfigureAwait(false);
    }
}
