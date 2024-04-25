using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Invite;

public class Invites(IServiceProvider services) : IInvites
{
    private IChats? _chats;
    private IPlaces? _places;
    private IAccounts? _accounts;
    private MomentClockSet? _clocks;
    private ILogger? _log;

    private IServiceProvider Services { get; } = services;
    private IInvitesBackend Backend { get; } = services.GetRequiredService<IInvitesBackend>();
    private IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    private IPlaces Places => _places ??= Services.GetRequiredService<IPlaces>();
    private IAccounts Accounts => _accounts ??= Services.GetRequiredService<IAccounts>();
    private ICommander Commander { get; } = services.Commander();
    private MomentClockSet Clocks => _clocks ??= Services.GetRequiredService<MomentClockSet>();
    private ILogger Log => _log ??= Services.LogFor<Invites>();

    // [ComputeMethod]
    public virtual async Task<ApiArray<Invite>> ListUserInvites(
        Session session,
        CancellationToken cancellationToken)
    {
        await AssertCanListUserInvites(session, cancellationToken).ConfigureAwait(false);

        var searchKey = new UserInviteOption().GetSearchKey();
        return await Backend.GetAll(searchKey, 1, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<Invite>> ListChatInvites(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        await AssertCanListChatInvites(session, chatId, cancellationToken).ConfigureAwait(false);

        var searchKey = new ChatInviteOption(chatId).GetSearchKey();
        return await Backend.GetAll(searchKey, 1, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<Invite>> ListPlaceInvites(
        Session session,
        PlaceId placeId,
        CancellationToken cancellationToken)
    {
        await AssertCanListPlaceInvites(session, placeId, cancellationToken).ConfigureAwait(false);

        var searchKey = new PlaceInviteOption(placeId).GetSearchKey();
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
        var minInviteLifespan = TimeSpan.FromHours(1);
        var minExpiresAt = Clocks.SystemClock.Now - minInviteLifespan;
        var invite = invites.Where(x => x.ExpiresOn > minExpiresAt && x.Remaining >= 1).MaxBy(c => c.ExpiresOn);
        if (invite == null) {
            invite = Invite.New(Constants.Invites.Defaults.ChatRemaining, new ChatInviteOption(chatId));
            invite = await Commander
                .Call(new Invites_Generate(session, invite), true, cancellationToken)
                .ConfigureAwait(false);
        }
        AutoInvalidate(invite);
        return invite;

        void AutoInvalidate(Invite invite1) {
            var delay = invite1.ExpiresOn - Clocks.SystemClock.Now - minInviteLifespan + TimeSpan.FromSeconds(1);
            // We don't want to reference Computed<T> for too long
            delay = delay.Clamp(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10));
            Computed.GetCurrent().Invalidate(delay);
        }
    }

    // [CommandHandler]
    public virtual async Task<Invite> OnGenerate(Invites_Generate command, CancellationToken cancellationToken)
    {
        var (session, invite) = command;
        var account = await AssertCanGenerate(session, invite, cancellationToken).ConfigureAwait(false);

        invite = command.Invite with { CreatedBy = account.Id };
        var generateCommand = new InvitesBackend_Generate(invite);
        return await Commander.Call(generateCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Invite> OnUse(
        Invites_Use command,
        CancellationToken cancellationToken)
    {
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
        var (session, inviteId) = command;
        var invite = await Backend.Get(inviteId, cancellationToken).ConfigureAwait(false);
        invite.Require();

        _ = await AssertCanRevoke(session, invite, cancellationToken).ConfigureAwait(false);
        var revokeCommand = new InvitesBackend_Revoke(session, invite.Id);
        await Commander.Call(revokeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Assertions

    private async Task AssertCanListUserInvites(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
    }

    private async Task AssertCanListChatInvites(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        rules.Require(ChatPermissions.Invite);
    }

    private async Task AssertCanListPlaceInvites(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        var rules = await Places.GetRules(session, placeId, cancellationToken).ConfigureAwait(false);
        rules.Require(PlacePermissions.Invite);
    }

    private async Task<AccountFull> AssertCanGenerate(Session session, Invite invite, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(Account.MustNotBeGuest);
        account.Require(AccountFull.MustBeActive);

        switch (invite.Details.Option) {
        case UserInviteOption:
            if (!account.IsAdmin)
                throw StandardError.Unauthorized("Only admins can generate user invites.");
            break;
        case ChatInviteOption chatInvite:
            var rulesChat = await Chats
                .GetRules(session, chatInvite.ChatId, cancellationToken)
                .ConfigureAwait(false);
            rulesChat.Require(ChatPermissions.Invite);
            break;
        case PlaceInviteOption placeInvite:
            var rulesPlace = await Places
                .GetRules(session, placeInvite.PlaceId, cancellationToken)
                .ConfigureAwait(false);
            rulesPlace.Require(PlacePermissions.Invite);
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

        switch (invite.Details.Option) {
        case UserInviteOption:
            if (!account.IsAdmin)
                throw StandardError.Unauthorized("Only admins can revoke user invites.");
            break;
        case ChatInviteOption chatInvite:
            var rules = await Chats
                .GetRules(session, chatInvite.ChatId, cancellationToken)
                .ConfigureAwait(false);
            rules.Require(ChatPermissions.Invite);
            break;
        default:
            throw StandardError.Format<Invite>();
        }

        return account;
    }
}
