namespace ActualChat.Invite;

/// <summary>
/// Server-side adapter that delegates legacy v2.7 invite RPC calls to the modern
/// <see cref="IInvites"/> service and projects the responses into the v2.7
/// wire-frozen <see cref="LegacyInvite"/> shape.
/// </summary>
public class LegacyInvites(IServiceProvider services) : ILegacyInvites
{
    private IInvites Invites { get; } = services.GetRequiredService<IInvites>();
    private ICommander Commander { get; } = services.Commander();

#pragma warning disable CS0618 // ListUserInvites is itself obsolete
    public virtual async Task<LegacyInvite[]> ListLegacyUserInvites(
        Session session, CancellationToken cancellationToken)
    {
        var invites = await Invites.ListUserInvites(session, cancellationToken).ConfigureAwait(false);
        return invites.Select(LegacyInvite.From).ToArray();
    }
#pragma warning restore CS0618

    public virtual async Task<LegacyInvite[]> ListLegacyChatInvites(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var invites = await Invites.ListChatInvites(session, chatId, cancellationToken).ConfigureAwait(false);
        return invites.Select(LegacyInvite.From).ToArray();
    }

    public virtual async Task<LegacyInvite[]> ListLegacyPlaceInvites(
        Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        var invites = await Invites.ListPlaceInvites(session, placeId, cancellationToken).ConfigureAwait(false);
        return invites.Select(LegacyInvite.From).ToArray();
    }

    public virtual async Task<LegacyInvite?> GetOrGenerateLegacyChatInvite(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var invite = await Invites.GetOrGenerateChatInvite(session, chatId, cancellationToken).ConfigureAwait(false);
        return invite is null ? null : LegacyInvite.From(invite);
    }

    public virtual async Task<LegacyInvite?> GetOrGenerateLegacyPlaceInvite(
        Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        var invite = await Invites.GetOrGeneratePlaceInvite(session, placeId, cancellationToken).ConfigureAwait(false);
        return invite is null ? null : LegacyInvite.From(invite);
    }

    public virtual async Task<LegacyInvite> OnLegacyGenerate(
        LegacyInvites_Generate command, CancellationToken cancellationToken)
    {
        var modern = new Invites_Generate(command.Session, command.Invite.ToModern());
        var invite = await Commander.Call(modern, true, cancellationToken).ConfigureAwait(false);
        return LegacyInvite.From(invite);
    }

    public virtual async Task<LegacyInvite> OnLegacyUse(
        Invites_Use command, CancellationToken cancellationToken)
    {
        var invite = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        return LegacyInvite.From(invite);
    }

    public virtual Task OnLegacyRevoke(Invites_Revoke command, CancellationToken cancellationToken)
        => Commander.Call(command, true, cancellationToken);
}
