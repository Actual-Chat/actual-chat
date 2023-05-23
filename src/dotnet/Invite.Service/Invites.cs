using ActualChat.Chat;
using ActualChat.Invite.Backend;
using ActualChat.Users;

namespace ActualChat.Invite;

internal class Invites : IInvites
{
    private IChats? _chats;

    private IServiceProvider Services { get; }
    private IAccounts Accounts { get; }
    private IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    private ICommander Commander { get; }
    private IInvitesBackend Backend { get; }

    public Invites(IServiceProvider services)
    {
        Services = services;
        Accounts = services.GetRequiredService<IAccounts>();
        Commander = services.Commander();
        Backend = services.GetRequiredService<IInvitesBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Invite>> ListUserInvites(
        Session session,
        CancellationToken cancellationToken)
    {
        await AssertCanListUserInvites(session, cancellationToken).ConfigureAwait(false);

        var searchKey = new UserInviteOption().GetSearchKey();
        return await Backend.GetAll(searchKey, 1, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Invite>> ListChatInvites(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        await AssertCanListChatInvites(session, chatId, cancellationToken).ConfigureAwait(false);

        var searchKey = new ChatInviteOption(chatId).GetSearchKey();
        return await Backend.GetAll(searchKey, 1, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Invite> Generate(IInvites.GenerateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var (session, invite) = command;
        var account = await AssertCanGenerate(session, invite, cancellationToken).ConfigureAwait(false);

        invite = command.Invite with { CreatedBy = account.Id };
        return await Commander.Call(new IInvitesBackend.GenerateCommand(invite), cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Invite> Use(
        IInvites.UseCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(Account.MustNotBeGuest);

        var useCommand = new IInvitesBackend.UseCommand(command.Session, command.InviteId);
        var invite = await Commander.Call(useCommand, cancellationToken).ConfigureAwait(false);
        return invite.Mask();
    }

    // [CommandHandler]
    public virtual async Task Revoke(IInvites.RevokeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var (session, inviteId) = command;
        var invite = await Backend.Get(inviteId, cancellationToken).ConfigureAwait(false);
        invite.Require();
        _ = await AssertCanRevoke(session, invite, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new IInvitesBackend.RevokeCommand(session, invite.Id), cancellationToken)
            .ConfigureAwait(false);
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
