using ActualChat.Kvas;

namespace ActualChat.Users;

public class Avatars : IAvatars
{
    private IAccounts Accounts { get; }
    private IAvatarsBackend Backend { get; }
    private IServerKvas ServerKvas { get; }
    private ICommander Commander { get; }

    public Avatars(IServiceProvider services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Backend = services.GetRequiredService<IAvatarsBackend>();
        ServerKvas = services.ServerKvas();
        Commander = services.Commander();
    }

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
    public virtual async Task<ApiArray<Symbol>> ListOwnAvatarIds(Session session, CancellationToken cancellationToken)
    {
        var kvasClient = ServerKvas.GetClient(session);
        var settings = await kvasClient.GetUserAvatarSettings(cancellationToken).ConfigureAwait(false);
        return settings.AvatarIds;
    }

    // [CommandHandler]
    public virtual async Task<AvatarFull> OnChange(Avatars_Change command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var (session, avatarId, expectedVersion, change) = command;
        command.Change.RequireValid();

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (change.IsCreate(out var avatar)) {
            // Create: fill in all missing properties
            change = new Change<AvatarFull>() {
                Create = avatar.WithMissingPropertiesFrom(account.Avatar),
            };
        }
        else {
            // Update or remove: ensure the avatar exists & belongs to the current user
            await GetOwn(session, avatarId, cancellationToken).Require().ConfigureAwait(false);
        }

        var changeCommand = new AvatarsBackend_Change(avatarId, expectedVersion, change);
        avatar = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);

        if (avatar.IsAnonymous)
            return avatar; // We don't account anonymous avatars in the list

        cancellationToken = default; // We don't cancel anything from here
        var kvas = ServerKvas.GetClient(session);
        var oldSettings = await kvas.GetUserAvatarSettings(cancellationToken).ConfigureAwait(false);
        var settings = oldSettings;
        if (change.Create.HasValue)
            settings = settings.WithAvatarId(avatar.Id);
        else if (change.Remove)
            settings = settings.WithoutAvatarId(avatarId);
        if (!ReferenceEquals(settings, oldSettings))
            await kvas.SetUserAvatarSettings(settings, cancellationToken).ConfigureAwait(false);

        return avatar;
    }

    // [CommandHandler]
    public virtual async Task OnSetDefault(Avatars_SetDefault command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var (session, avatarId) = command;
        var avatar = await GetOwn(session, avatarId, cancellationToken).Require().ConfigureAwait(false);
        var kvas = ServerKvas.GetClient(session);
        var settings = await kvas.GetUserAvatarSettings(cancellationToken).ConfigureAwait(false);
        if (settings.DefaultAvatarId == avatar.Id)
            return;
        settings = settings with { DefaultAvatarId = avatarId };
        await kvas.SetUserAvatarSettings(settings, cancellationToken).ConfigureAwait(false);
    }
}
