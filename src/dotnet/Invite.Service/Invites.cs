namespace ActualChat.Invite;

/// <summary>
/// Frontend service for managing chat and place invite links with session-based access control.
/// </summary>
public class Invites(IServiceProvider services) : IInvites
{
    private static readonly TimeSpan MinInviteLifespan = TimeSpan.FromHours(1);

    private IServiceProvider Services { get; } = services;
    private IInvitesBackend Backend { get; } = services.GetRequiredService<IInvitesBackend>();
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IPlaces Places => field ??= Services.GetRequiredService<IPlaces>();
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private ICommander Commander { get; } = services.Commander();
    private MomentClockSet Clocks => field ??= Services.GetRequiredService<MomentClockSet>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    // [ComputeMethod]
    public virtual async Task<Invite[]> ListChatInvites(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        await AssertCanListChatInvites(session, chatId, cancellationToken).ConfigureAwait(false);

        var searchKey = ChatInvite.GetSearchKey(chatId);
        return await Backend.GetAll(searchKey, 1, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Invite[]> ListPlaceInvites(
        Session session,
        PlaceId placeId,
        CancellationToken cancellationToken)
    {
        await AssertCanListPlaceInvites(session, placeId, cancellationToken).ConfigureAwait(false);

        var searchKey = PlaceInvite.GetSearchKey(placeId);
        return await Backend.GetAll(searchKey, 1, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Invite?> GetOrGenerateChatInvite(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null || !chat.Rules.CanInvite())
            return null;

        var invites = await ListChatInvites(session, chatId, cancellationToken).ConfigureAwait(false);
        var invite = ChooseInvite(invites, Clocks.SystemClock.Now, MinInviteLifespan);
        if (invite == null) {
            invite = ChatInvite.New(Constants.Invites.Defaults.ChatRemaining, chatId);
            invite = await Commander
                .Call(new Invites_Generate { Session = session, Invite = invite }, true, cancellationToken)
                .ConfigureAwait(false);
        }

        AutoInvalidate(invite, MinInviteLifespan);
        return invite;
    }

    // [ComputeMethod]
    public virtual async Task<Invite?> GetOrGeneratePlaceInvite(
        Session session,
        PlaceId placeId,
        CancellationToken cancellationToken)
    {
        var place = await Places.Get(session, placeId, cancellationToken).ConfigureAwait(false);
        if (place == null || !place.Rules.CanInvite())
            return null;

        var invites = await ListPlaceInvites(session, placeId, cancellationToken).ConfigureAwait(false);
        var invite = ChooseInvite(invites, Clocks.SystemClock.Now, MinInviteLifespan);
        if (invite == null) {
            invite = PlaceInvite.New(Constants.Invites.Defaults.PlaceRemaining, placeId);
            invite = await Commander
                .Call(new Invites_Generate { Session = session, Invite = invite }, true, cancellationToken)
                .ConfigureAwait(false);
        }

        AutoInvalidate(invite, MinInviteLifespan);
        return invite;
    }

    // [ComputeMethod]
    public virtual async Task<InviteChatLinkPreview?> GetInviteChatLinkPreview(
        Session session,
        string inviteId,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Backend.GetInviteChatLinkPreview(account.Id, inviteId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Invite> OnGenerate(Invites_Generate command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var invite = command.Invite;
        var account = await AssertCanGenerate(session, invite, cancellationToken).ConfigureAwait(false);

        invite = command.Invite with { CreatedBy = account.Id.Value };
        var generateCommand = new InvitesBackend_Generate(invite);
        return await Commander.Call(generateCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Invite> OnUse(
        Invites_Use command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        Log.LogInformation("On Invites_Use");
        Exception? exception = null;
        try {
            var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
            account.Require(Account.MustNotBeGuest);

            var useCommand = new InvitesBackend_Use(command.Session, command.InviteId);
            var invite = await Commander.Call(useCommand, true, cancellationToken).ConfigureAwait(false);
            return invite.Mask();
        }
        catch (Exception e) {
            exception = e;
            throw;
        }
        finally {
            Log.LogInformation("On Invites_Use completed. Error: {Error}", exception);
        }
    }

    // [CommandHandler]
    public virtual async Task OnRevoke(Invites_Revoke command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var inviteId = command.InviteId;
        var invite = await Backend.Get(inviteId, cancellationToken).ConfigureAwait(false);
        invite.Require();

        _ = await AssertCanRevoke(session, invite, cancellationToken).ConfigureAwait(false);
        var revokeCommand = new InvitesBackend_Revoke(session, invite.Id);
        await Commander.Call(revokeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Assertions

    private Task AssertCanListChatInvites(Session session, ChatId chatId, CancellationToken cancellationToken)
        => RequireCanInvite(session, chatId, cancellationToken);

    private Task AssertCanListPlaceInvites(Session session, PlaceId placeId, CancellationToken cancellationToken)
        => RequireCanInvite(session, placeId, cancellationToken);

    private async Task RequireCanInvite(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chatRules = await Chats
            .GetRules(session, chatId, cancellationToken)
            .ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Invite);
    }

    private async Task RequireCanInvite(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        var placeRules = await Places
            .GetRules(session, placeId, cancellationToken)
            .ConfigureAwait(false);
        placeRules.Require(PlacePermissions.Invite);
    }

    private async Task<AccountFull> AssertCanGenerate(Session session, Invite invite, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(Account.MustNotBeGuest);
        account.Require(AccountFull.MustBeActive);

        switch (invite) {
        case UserInvite:
            throw StandardError.Constraint("User invites feature is removed.");
        case ChatInvite chatInvite:
            await RequireCanInvite(session, chatInvite.ChatId, cancellationToken).ConfigureAwait(false);
            break;
        case PlaceInvite placeInvite:
            await RequireCanInvite(session, placeInvite.PlaceId, cancellationToken).ConfigureAwait(false);
            break;
        default:
            throw StandardError.Format<Invite>();
        }

        return account;
    }

    private async Task<AccountFull> AssertCanRevoke(Session session, Invite invite, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(Account.MustNotBeGuest);
        account.Require(AccountFull.MustBeActive);

        var isCreatedByOwnAccount = invite.CreatedBy == account.Id.Value;
        switch (invite) {
        case UserInvite:
            throw StandardError.Constraint("User invites feature is removed.");
        case ChatInvite chatInvite:
            if (!isCreatedByOwnAccount)
                await RequireCanModerate(session, chatInvite.ChatId, cancellationToken).ConfigureAwait(false);

            break;
        case PlaceInvite placeInvite:
            if (!isCreatedByOwnAccount)
                await RequireCanModerate(session, placeInvite.PlaceId, cancellationToken).ConfigureAwait(false);

            break;
        default:
            throw StandardError.Format<Invite>();
        }

        return account;
    }

    private async Task RequireCanModerate(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chatRules = await Chats
            .GetRules(session, chatId, cancellationToken)
            .ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Moderate);
    }

    private async Task RequireCanModerate(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        var placeRules = await Places
            .GetRules(session, placeId, cancellationToken)
            .ConfigureAwait(false);
        placeRules.Require(PlacePermissions.Moderate);
    }

    internal static Invite? ChooseInvite(Invite[] invites, Moment now, TimeSpan minInviteLifespan)
    {
        var minExpiresAt = now + minInviteLifespan;
        return invites
            .Where(x => (x.ExpiresOn == default || x.ExpiresOn > minExpiresAt) && x.Remaining >= 1)
            .MaxBy(c => c.ExpiresOn);
    }

    private void AutoInvalidate(Invite invite1, TimeSpan minInviteLifespan)
    {
        // The delay is clamped - we don't want to reference Computed<T> for too long
        var delay = invite1.ExpiresOn - Clocks.SystemClock.Now - minInviteLifespan + TimeSpan.FromSeconds(1);
        delay = delay.Clamp(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10));
        Computed.GetCurrent().Invalidate(delay);
    }
}
