
namespace ActualChat.Users;

/// <summary>
/// Frontend service for managing user avatars with session-based access control.
/// </summary>
public class Avatars(IServiceProvider services) : IAvatars
{
    private IServiceProvider Services { get; } = services;
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAvatarsBackend Backend { get; } = services.GetRequiredService<IAvatarsBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<AvatarFull?> GetOwn(Session session, Symbol avatarId, CancellationToken cancellationToken)
    {
        var avatar = await Backend.Get(avatarId, cancellationToken).ConfigureAwait(false);
        if (avatar == null)
            return null;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (avatar.UserId != account.Id)
            return null;
        return avatar;
    }

    // [ComputeMethod]
    public virtual async Task<Avatar?> Get(Session session, Symbol avatarId, CancellationToken cancellationToken)
    {
        var avatar = await Backend.Get(avatarId, cancellationToken).ConfigureAwait(false);
        return avatar?.ToAvatar();
    }

    // [ComputeMethod]
    public virtual async Task<IReadOnlyList<Symbol>> ListOwnAvatarIds(Session session, CancellationToken cancellationToken)
    {
        var userAvatarSettings = await Services.UserSettingsUI(session)
            .UserAvatarSettings().Get(cancellationToken)
            .ConfigureAwait(false);
        return userAvatarSettings.AvatarIds.ToArray();
    }

    // [CommandHandler]
    public virtual async Task<AvatarFull> OnChange(Avatars_Change command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var avatarId = command.AvatarId;
        var expectedVersion = command.ExpectedVersion;
        var change = command.Change;
        command.Change.RequireValid();

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (change.IsCreate(out var diff)) {
            // Create: fill in missing properties from account avatar
            var accountAvatar = new AvatarFull(account.Id).WithMissingPropertiesFrom(account.Avatar);
            diff = diff.WithMissingPropertiesFrom(accountAvatar) with { UserId = account.Id };
            change = new Change<AvatarDiff> { Create = diff };
        }
        else
            // Update or remove: ensure the avatar exists & belongs to the current user
            await GetOwn(session, avatarId, cancellationToken).Require().ConfigureAwait(false);

        var changeCommand = new AvatarsBackend_Change(avatarId, expectedVersion, change);
        var avatar = await Commander.Call(changeCommand, cancellationToken).ConfigureAwait(false);

        if (avatar.IsAnonymous)
            return avatar; // We don't account anonymous avatars in the list

        await UpdateAvatarList(
            Services.UserSettingsUI(session),
            change.Remove ? avatarId : avatar.Id, change
            ).ConfigureAwait(false);
        return avatar;
    }

    // [CommandHandler]
    public virtual async Task OnSetDefault(Avatars_SetDefault command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var avatarId = command.AvatarId;
        var userSettingsUI = Services.UserSettingsUI(session);

        var avatar = await GetOwn(session, avatarId, cancellationToken).Require().ConfigureAwait(false);
        var userAvatarSettings = await userSettingsUI
            .UserAvatarSettings().Get(cancellationToken)
            .ConfigureAwait(false);
        if (userAvatarSettings.DefaultAvatarId == avatar.Id)
            return;

        userAvatarSettings = userAvatarSettings with { DefaultAvatarId = avatarId };
        await userSettingsUI.UserAvatarSettings().Set(userAvatarSettings, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task UpdateAvatarList(
        UserSettingsUI userSettingsUI,
        Symbol avatarId,
        Change<AvatarDiff> change)
    {
        // We don't cancel anything from here
        CancellationToken cancellationToken = default;
        var oldSettings = await userSettingsUI.UserAvatarSettings().Get(cancellationToken).ConfigureAwait(false);
        var settings = oldSettings;
        if (change.Create.HasValue)
            settings = settings.WithAvatarId(avatarId);
        else if (change.Remove)
            settings = settings.WithoutAvatarId(avatarId);
        if (!ReferenceEquals(settings, oldSettings))
            await userSettingsUI.UserAvatarSettings().Set(settings, cancellationToken).ConfigureAwait(false);
    }
}
