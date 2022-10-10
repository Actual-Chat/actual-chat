using ActualChat.Chat;
using ActualChat.Invite.Backend;
using ActualChat.Users;

namespace ActualChat.Invite;

internal class Invites : IInvites
{
    private readonly IInvitesBackend _backend;
    private readonly ICommander _commander;
    private readonly IAuth _auth;
    private readonly IAccounts _accounts;
    private readonly IChats _chats;

    public Invites(
        IInvitesBackend backend,
        ICommander commander,
        IAuth auth,
        IChats chats,
        IAccounts accounts)
    {
        _backend = backend;
        _commander = commander;
        _auth = auth;
        _chats = chats;
        _accounts = accounts;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Invite>> ListUserInvites(
        Session session,
        CancellationToken cancellationToken)
    {
        await AssertCanListUserInvites(session, cancellationToken).ConfigureAwait(false);

        var details = new InviteDetails() { User = new UserInviteDetails() };
        return await _backend.GetAll(details.GetSearchKey(), 1, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Invite>> ListChatInvites(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        await AssertCanListChatInvites(session, chatId, cancellationToken).ConfigureAwait(false);

        var details = new InviteDetails() { Chat = new ChatInviteDetails(chatId) };
        return await _backend.GetAll(details.GetSearchKey(), 1, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Invite> Generate(IInvites.GenerateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var (session, invite) = command;
        var account = await AssertCanGenerate(session, invite, cancellationToken).ConfigureAwait(false);

        invite = command.Invite with { CreatedBy = account.Id };
        return await _commander.Call(new IInvitesBackend.GenerateCommand(invite), cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Invite> Use(
        IInvites.UseCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return null!;

        var cmd = new IInvitesBackend.UseCommand(command.Session, command.InviteId);
        var invite = await _commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        return invite.Mask();
    }

    // Assertions

    private Task AssertCanListUserInvites(Session session, CancellationToken cancellationToken)
        => _accounts.Get(session, cancellationToken)
            .Require(Account.MustBeAdmin);

    private async Task AssertCanListChatInvites(Session session, string chatId, CancellationToken cancellationToken)
    {
        var rules = await _chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        rules.Require(ChatPermissions.Invite);
    }

    private async Task<Account> AssertCanGenerate(Session session, Invite invite, CancellationToken cancellationToken)
    {
        var account = await _accounts.Get(session, cancellationToken)
            .Require(Account.MustBeActive)
            .ConfigureAwait(false);

        var userInviteDetails = invite.Details?.User;
        if (userInviteDetails != null) {
            if (!account.IsAdmin)
                throw StandardError.Unauthorized("Only admins can generate user invites.");
        }

        var chatInviteDetails = invite.Details?.Chat;
        if (chatInviteDetails != null) {
            var rules = await _chats
                .GetRules(session, chatInviteDetails.ChatId, cancellationToken)
                .ConfigureAwait(false);
            rules.Require(ChatPermissions.Invite);
        }

        return account;
    }
}
