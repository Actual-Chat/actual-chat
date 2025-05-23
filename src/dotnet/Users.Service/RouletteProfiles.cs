using ActualChat.Kvas;
using ActualChat.Roulette;

namespace ActualChat.Users;

public class RouletteProfiles(IServiceProvider services) : IRouletteProfiles
{
    private const string SelectedRouletteProfileIdKvasKey = "Roulette.SelectedProfileId";

    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAvatars Avatars { get; } = services.GetRequiredService<IAvatars>();
    private IRouletteProfilesBackend Backend { get; } = services.GetRequiredService<IRouletteProfilesBackend>();
    private IServerKvas ServerKvas { get; } = services.ServerKvas();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<Symbol> GetSelectedProfileId(Session session, CancellationToken cancellationToken)
    {
        var kvas = ServerKvas.GetClient(session);
        var selectedProfileId = await kvas.Get<string>(SelectedRouletteProfileIdKvasKey, cancellationToken).ConfigureAwait(false);
        if (selectedProfileId is null)
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

    // [ComputeMethod]
    public virtual async Task<Profile?> GetProfile(
        Session session,
        Symbol profileId,
        CancellationToken cancellationToken)
    {
        if (profileId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(profileId));

        // NOTE(DF): Do we need any permission checks here?

        var profile = await Backend.GetProfile(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
            return null;

        return profile.ToProfile();
    }

    // [ComputeMethod]
    public virtual async Task<RouletteUserSettings?> GetOwnUserSettings(
        Session session,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var settings = await Backend.GetUserSettings(account.Id, cancellationToken).ConfigureAwait(false);
        return settings;
    }

    // Commands

    // [CommandHandler]
    public virtual async Task<Profile> OnChange(RouletteProfiles_UpsertProfile command, CancellationToken cancellationToken)
    {
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

            var preferencesFull = new ProfilePreferencesFull(avatar.UserId, profileId, profile.Preferences.Version)
                .WithMissingPropertiesFrom(profile.Preferences);
            var prefsChange = new RouletteProfilesBackend_ChangePrefs(profileId, null, Change.Create(preferencesFull));
            var preferences = await Commander.Call(prefsChange, cancellationToken).ConfigureAwait(false);

            var kvas = ServerKvas.GetClient(session);
            await ActualChat.Users.Avatars.UpdateAvatarList(kvas, avatarsChange.Change, avatar.Id).ConfigureAwait(false);

            return Profile.New(avatar.ToAvatar(), preferences);
        }
        else {
            var avatar = await Avatars.GetOwn(session, profileId, cancellationToken).ConfigureAwait(false);
            if (avatar is null)
                throw StandardError.Constraint("Invalid profile id.");

            var profile = command.Change.Update.Value;
            avatar = new AvatarFull(avatar.UserId, avatar.Id).WithMissingPropertiesFrom(profile.Avatar);
            var avatarsChange = new Avatars_Change(session, profileId, profile.Avatar.Version, Change.Update(avatar));
            avatar = await Commander.Call(avatarsChange, cancellationToken).ConfigureAwait(false);

            var preferencesFull = new ProfilePreferencesFull(avatar.UserId, profileId, profile.Preferences.Version)
                .WithMissingPropertiesFrom(profile.Preferences);
            var prefsChange = preferencesFull.IsStored()
                ? new RouletteProfilesBackend_ChangePrefs(profileId, preferencesFull.Version, Change.Update(preferencesFull))
                : new RouletteProfilesBackend_ChangePrefs(profileId, null, Change.Create(preferencesFull));
            preferencesFull = await Commander.Call(prefsChange, cancellationToken).ConfigureAwait(false);

            return Profile.New(avatar, preferencesFull.ToProfilePreferences());
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
        await kvas.Set(SelectedRouletteProfileIdKvasKey, profileId.Value, cancellationToken).ConfigureAwait(false);
    }

    [CommandHandler]
    public virtual async Task<RouletteUserSettings?> OnChangeOwnUserSettings(
        RouletteProfiles_ChangeOwnUserSettings command,
        CancellationToken cancellationToken)
    {
        var (session, expectedVersion, change) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var backendCommand = new RouletteProfilesBackend_ChangeUserSettings(account.Id, expectedVersion, change);
        return await Commander.Call(backendCommand, cancellationToken).ConfigureAwait(false);
    }
}
