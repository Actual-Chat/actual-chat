using ActualChat.Chat;
using ActualChat.Invite.Backend;
using ActualChat.Users;

namespace ActualChat.Invite;

internal class Invites : IInvites
{
    private IAccounts Accounts { get; }
    private IChats Chats { get; }
    private ICommander Commander { get; }
    private IInvitesBackend Backend { get; }

    public Invites(IServiceProvider services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Chats = services.GetRequiredService<IChats>();
        Commander = services.Commander();
        Backend = services.GetRequiredService<IInvitesBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Invite>> ListUserInvites(
        Session session,
        CancellationToken cancellationToken)
    {
        await AssertCanListUserInvites(session, cancellationToken).ConfigureAwait(false);

        var inviteDetails = new InviteDetails() { User = new UserInviteDetails() };
        return await Backend.GetAll(inviteDetails.GetSearchKey(), 1, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Invite>> ListChatInvites(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        await AssertCanListChatInvites(session, chatId, cancellationToken).ConfigureAwait(false);

        var inviteDetails = new InviteDetails() { Chat = new ChatInviteDetails(chatId) };
        return await Backend.GetAll(inviteDetails.GetSearchKey(), 1, cancellationToken).ConfigureAwait(false);
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

        // Invites work only once you sign in
        await Accounts.Get(command.Session, cancellationToken).Require().ConfigureAwait(false);

        var cmd = new IInvitesBackend.UseCommand(command.Session, command.InviteId);
        var invite = await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        return invite.Mask();
    }

    // Assertions

    private Task AssertCanListUserInvites(Session session, CancellationToken cancellationToken)
        => Accounts.Get(session, cancellationToken)
            .Require(Account.MustBeAdmin);

    private async Task AssertCanListChatInvites(Session session, string chatId, CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        rules.Require(ChatPermissions.Invite);
    }

    private async Task<Account> AssertCanGenerate(Session session, Invite invite, CancellationToken cancellationToken)
    {
        var account = await Accounts.Get(session, cancellationToken)
            .Require(Account.MustBeActive)
            .ConfigureAwait(false);

        if (invite.Details.User != null) {
            if (!account.IsAdmin)
                throw StandardError.Unauthorized("Only admins can generate user invites.");
        }
        if (invite.Details.Chat is { } chatDetails) {
            var rules = await Chats
                .GetRules(session, chatDetails.ChatId, cancellationToken)
                .ConfigureAwait(false);
            rules.Require(ChatPermissions.Invite);
        }

        return account;
    }
}
