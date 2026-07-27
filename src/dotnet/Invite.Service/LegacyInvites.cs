using ActualChat.Logging;

namespace ActualChat.Invite;

/// <summary>
/// Server-side adapter that delegates legacy v2.7 invite RPC calls to the modern
/// <see cref="IInvites"/> service and projects the responses into the v2.7
/// wire-frozen <see cref="LegacyInvite"/> shape.
/// </summary>
public class LegacyInvites(IServiceProvider services) : ILegacyInvites
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IInvites Invites { get; } = services.GetRequiredService<IInvites>();
    private ICommander Commander { get; } = services.Commander();
    private ILogger Log { get; } = services.LogFor<LegacyInvites>();

#pragma warning disable CS0618 // ListUserInvites is itself obsolete
    public virtual async Task<LegacyInvite[]> ListUserInvites(
        Session session, CancellationToken cancellationToken)
    {
        await LogUsage(nameof(ILegacyInvites.ListUserInvites), session, cancellationToken).ConfigureAwait(false);
        var invites = await Invites.ListUserInvites(session, cancellationToken).ConfigureAwait(false);
        return invites.Select(LegacyInvite.From).ToArray();
    }
#pragma warning restore CS0618

    public virtual async Task<LegacyInvite[]> ListChatInvites(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        await LogUsage(nameof(ILegacyInvites.ListChatInvites), session, cancellationToken).ConfigureAwait(false);
        var invites = await Invites.ListChatInvites(session, chatId, cancellationToken).ConfigureAwait(false);
        return invites.Select(LegacyInvite.From).ToArray();
    }

    public virtual async Task<LegacyInvite[]> ListPlaceInvites(
        Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        await LogUsage(nameof(ILegacyInvites.ListPlaceInvites), session, cancellationToken).ConfigureAwait(false);
        var invites = await Invites.ListPlaceInvites(session, placeId, cancellationToken).ConfigureAwait(false);
        return invites.Select(LegacyInvite.From).ToArray();
    }

    public virtual async Task<LegacyInvite?> GetOrGenerateChatInvite(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        await LogUsage(nameof(ILegacyInvites.GetOrGenerateChatInvite), session, cancellationToken)
            .ConfigureAwait(false);
        var invite = await Invites.GetOrGenerateChatInvite(session, chatId, cancellationToken).ConfigureAwait(false);
        return invite is null ? null : LegacyInvite.From(invite);
    }

    public virtual async Task<LegacyInvite?> GetOrGeneratePlaceInvite(
        Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        await LogUsage(nameof(ILegacyInvites.GetOrGeneratePlaceInvite), session, cancellationToken)
            .ConfigureAwait(false);
        var invite = await Invites.GetOrGeneratePlaceInvite(session, placeId, cancellationToken).ConfigureAwait(false);
        return invite is null ? null : LegacyInvite.From(invite);
    }

    public virtual async Task<LegacyInvite> OnGenerate(
        LegacyInvites_Generate command, CancellationToken cancellationToken)
    {
        await LogUsage(nameof(ILegacyInvites.OnGenerate), command.Session, cancellationToken).ConfigureAwait(false);
        var modern = new Invites_Generate(command.Session, command.Invite.ToModern());
        var invite = await Commander.Call(modern, true, cancellationToken).ConfigureAwait(false);
        return LegacyInvite.From(invite);
    }

    public virtual async Task<LegacyInvite> OnUse(
        LegacyInvites_Use command, CancellationToken cancellationToken)
    {
        await LogUsage(nameof(ILegacyInvites.OnUse), command.Session, cancellationToken).ConfigureAwait(false);
        var invite = await Commander.Call(command.ToModern(), true, cancellationToken).ConfigureAwait(false);
        return LegacyInvite.From(invite);
    }

    public virtual async Task OnRevoke(LegacyInvites_Revoke command, CancellationToken cancellationToken)
    {
        await LogUsage(nameof(ILegacyInvites.OnRevoke), command.Session, cancellationToken).ConfigureAwait(false);
        await Commander.Call(command.ToModern(), true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async ValueTask LogUsage(string method, Session session, CancellationToken cancellationToken)
    {
        var entryPoint = $"{nameof(ILegacyInvites)}.{method}";
        string? clientInfo = null;
        try {
            var sessionInfo = await Accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
            clientInfo = sessionInfo?.Description;
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to resolve client version for legacy API {EntryPoint}", entryPoint);
        }
        LegacyApiUsageLog.Write(Log, entryPoint, session, clientInfo);
    }
}
