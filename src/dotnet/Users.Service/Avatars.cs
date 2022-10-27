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
    public virtual async Task<Avatar?> Get(string avatarId, CancellationToken cancellationToken)
        => await Backend.Get(avatarId, cancellationToken).ConfigureAwait(false);

    // [ComputeMethod]
    public virtual async Task<AvatarFull?> GetOwn(Session session, string avatarId, CancellationToken cancellationToken)
    {
        var avatarIds = await ListAvatarIds(session, cancellationToken).ConfigureAwait(false);
        if (!avatarIds.Contains(avatarId))
            return null;

        var avatar = await Backend.Get(avatarId, cancellationToken).ConfigureAwait(false);
        return avatar;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListAvatarIds(Session session, CancellationToken cancellationToken)
    {
        var kvasClient = ServerKvas.GetClient(session);
        var settings = await kvasClient.GetUserAvatarSettings(cancellationToken).ConfigureAwait(false);
        return settings.AvatarIds;
    }

    // [CommandHandler]
    public virtual async Task<AvatarFull> Change(IAvatars.ChangeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var (session, avatarId, change) = command;
        command.Change.RequireValid();

        if (!change.Create.HasValue)
            await GetOwn(session, avatarId, cancellationToken).Require().ConfigureAwait(false);

        var cmd = new IAvatarsBackend.ChangeCommand(avatarId, change);
        var avatar = await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);

        cancellationToken = default; // We don't cancel anything from here
        var kvas = ServerKvas.GetClient(session);
        var oldSettings = await kvas.GetUserAvatarSettings(cancellationToken).ConfigureAwait(false);
        var settings = oldSettings;
        if (change.Create.HasValue)
            settings = settings.WithAvatarId(avatar.Id);
        else if (change.Remove)
            settings = settings.WithoutAvatarId(avatar.Id);
        if (!ReferenceEquals(settings, oldSettings))
            await kvas.SetUserAvatarSettings(settings, cancellationToken).ConfigureAwait(false);

        return avatar;
    }

    // [CommandHandler]
    public virtual async Task SetDefault(IAvatars.SetDefaultCommand command, CancellationToken cancellationToken)
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
