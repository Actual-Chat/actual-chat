using System.Security;
using ActualChat.Chat;
using ActualChat.Invite.Backend;
using ActualChat.Users;

namespace ActualChat.Invite;

internal class Invites : IInvites
{
    private readonly IInvitesBackend _backend;
    private readonly ICommander _commander;
    private readonly IAuth _auth;
    private readonly IChats _chats;
    private readonly IUserProfiles _userProfiles;

    public Invites(
        IInvitesBackend backend,
        ICommander commander,
        IAuth auth,
        IChats chats,
        IUserProfiles userProfiles)
    {
        _backend = backend;
        _commander = commander;
        _auth = auth;
        _chats = chats;
        _userProfiles = userProfiles;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Invite>> GetUserInvites(
        Session session,
        CancellationToken cancellationToken)
    {
        await AssertCanGetUserInvites(session, cancellationToken).ConfigureAwait(false);

        var details = new InviteDetails() { User = new UserInviteDetails() };
        return await _backend.GetAll(details.GetSearchKey(), 1, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Invite>> GetChatInvites(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        await AssertReadChatInvites(session, chatId, cancellationToken).ConfigureAwait(false);

        var details = new InviteDetails() { Chat = new ChatInviteDetails(chatId) };
        return await _backend.GetAll(details.GetSearchKey(), 1, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Invite> Generate(IInvites.GenerateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var (session, invite) = command;
        var userProfile = await AssertCanGenerate(session, invite, cancellationToken).ConfigureAwait(false);

        invite = command.Invite with { CreatedBy = userProfile.Id };
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

    private async Task AssertCanGetUserInvites(Session session, CancellationToken cancellationToken)
    {
        if (!await _auth.IsAdmin(session, cancellationToken).ConfigureAwait(false))
            throw new SecurityException("Not allowed to read user invites");
    }

    private async Task AssertReadChatInvites(Session session, string chatId, CancellationToken cancellationToken)
    {
        var rules = await _chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        rules.Demand(ChatPermissions.Invite);
    }

    private async Task<UserProfile> AssertCanGenerate(Session session, Invite invite, CancellationToken cancellationToken)
    {
        var userProfile = await _userProfiles.DemandActive(session, cancellationToken).ConfigureAwait(false);

        var userInviteDetails = invite.Details?.User;
        if (userInviteDetails != null) {
            if (!userProfile.IsAdmin)
                throw new SecurityException("Only admins can generate user invites");
        }

        var chatInviteDetails = invite.Details?.Chat;
        if (chatInviteDetails != null) {
            var rules = await _chats
                .GetRules(session, chatInviteDetails.ChatId, cancellationToken)
                .ConfigureAwait(false);
            rules.Demand(ChatPermissions.Invite);
        }

        return userProfile;
    }
}
