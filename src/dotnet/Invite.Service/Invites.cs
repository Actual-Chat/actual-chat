using System.Security;
using ActualChat.Chat;
using ActualChat.Invite.Backend;
using ActualChat.Users;

namespace ActualChat.Invite;

internal class Invites : IInvites
{
    private readonly IInvitesBackend _backend;
    private readonly IAuth _auth;
    private readonly IUserProfiles _userProfiles;
    private readonly IChats _chats;
    private readonly ILogger<Invites> _logger;
    private readonly ICommander _commander;

    public Invites(
        IInvitesBackend backend,
        ICommander commander,
        IAuth auth,
        ILogger<Invites> logger,
        IUserProfiles userProfiles,
        IChats chats)
    {
        _backend = backend;
        _commander = commander;
        _auth = auth;
        _logger = logger;
        _userProfiles = userProfiles;
        _chats = chats;
    }

    // [ComputeMethod]
    public virtual async Task<IImmutableList<Invite>> GetUserInvites(
        Session session,
        CancellationToken cancellationToken)
    {
        await AssertCanGetUserInvites(session, cancellationToken).ConfigureAwait(false);

        var invites = await _backend.GetAll(cancellationToken).ConfigureAwait(false);
        return invites.Where(x => x.Details?.User != null).ToImmutableArray();
    }

    // [ComputeMethod]
    public virtual async Task<IImmutableList<Invite>> GetChatInvites(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        await AssertReadChatInvites(session, chatId, cancellationToken).ConfigureAwait(false);
        var invites = await _backend.GetAll(cancellationToken).ConfigureAwait(false);
        return invites.Where(x => string.Equals(x.Details?.Chat?.ChatId, chatId, StringComparison.OrdinalIgnoreCase))
            .ToImmutableArray();
    }

    // [CommandHandler]
    public virtual async Task<Invite> Generate(IInvites.GenerateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var (session, invite) = command;
        await AssertCanGenerate(session, invite, cancellationToken).ConfigureAwait(false);
        var user = await _auth.GetUser(command.Session, cancellationToken).ConfigureAwait(false)
            ?? throw new Exception("User not found");
        invite = command.Invite with {
            CreatedBy = user.Id,
        };

        return await _commander.Call(new IInvitesBackend.GenerateCommand(invite), cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<InviteUsageResult> UseInvite(
        IInvites.UseInviteCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            return null!;
        }

        var (session, code) = command;

        var userProfile = await _userProfiles.Get(session, cancellationToken).ConfigureAwait(false)
            ?? throw new Exception("User profile not found");

        var invite = await _backend.GetByCode(code, cancellationToken).ConfigureAwait(false);
        if (invite == null)
            return InviteUsageResult.Fail("Incorrect invite code");

        var validationResult = await Validate(invite, userProfile, session, cancellationToken).ConfigureAwait(false);
        if (!validationResult.Succeeded)
            return validationResult;

        invite.Remaining--;
        await _commander.Call(new IInvitesBackend.UseInviteCommand(invite), cancellationToken).ConfigureAwait(false);

        await ProcessInviteKindSpecificLogic(invite, userProfile, session, cancellationToken).ConfigureAwait(false);

        return InviteUsageResult.Success(invite);
    }

    private async Task AssertCanGetUserInvites(Session session, CancellationToken cancellationToken)
    {
        if (!await _auth.IsAdmin(session, cancellationToken).ConfigureAwait(false))
            throw new SecurityException("Not allowed to read user invites");
    }

    private async Task AssertReadChatInvites(Session session, string chatId, CancellationToken cancellationToken)
    {
        var permissions = await _chats.GetPermissions(session, chatId, cancellationToken).ConfigureAwait(false);
        permissions.AssertHasPermissions(ChatPermissions.Invite);
    }

    private async Task AssertCanGenerate(Session session, Invite invite, CancellationToken cancellationToken)
    {
        if (invite.Details?.Chat != null) {
            var permissions = await _chats.GetPermissions(session, invite.Details.Chat.ChatId, cancellationToken)
                .ConfigureAwait(false);
            permissions.AssertHasPermissions(ChatPermissions.Invite);
        }

        if (invite.Details?.User != null) {
            if (!await _auth.IsAdmin(session, cancellationToken).ConfigureAwait(false))
                throw new SecurityException("Only admins can generate user invites");
        }
    }

    private async Task<InviteUsageResult> Validate(
        Invite invite,
        UserProfile userProfile,
        Session session,
        CancellationToken cancellationToken)
    {
        if (invite.Details?.User != null) {
            if (userProfile.Status == UserStatus.Suspended)
                return InviteUsageResult.Fail(
                    "Your account cannot be activated because your current status is suspended");
            if (userProfile.Status == UserStatus.Active)
                return InviteUsageResult.Fail("Your account is already active");
        }

        if (invite.Details?.Chat != null) {
            var chat = await _chats.Get(session, invite.Details.Chat.ChatId, cancellationToken).ConfigureAwait(false);
            if (chat == null)
                return InviteUsageResult.Fail("Chat not found");
        }

        if (invite.Remaining <= 0) {
            _logger.LogInformation("Invite code={InviteCode} is used up", invite.Code);
            return InviteUsageResult.Fail("This invite code have been used up");
        }

        return InviteUsageResult.Success(invite);
    }

    private async Task ProcessInviteKindSpecificLogic(
        Invite invite,
        UserProfile userProfile,
        Session session,
        CancellationToken cancellationToken)
    {
        if (invite.Details?.Chat != null) {
            var updateOptionCommand = new ISessionOptionsBackend.UpsertCommand(
                session,
                new ("InviteCode::Id", invite.Id.Value));
            await _commander.Call(updateOptionCommand, true, cancellationToken).ConfigureAwait(false);
            var updateOptionCommand2 = new ISessionOptionsBackend.UpsertCommand(
                session,
                new ("InviteCode::ChatId", invite.Details.Chat.ChatId.Value));
            await _commander.Call(updateOptionCommand2, true, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (invite.Details?.User != null) {
            userProfile.Status = UserStatus.Active;
            await _commander.Call(new IUserProfilesBackend.UpdateCommand(userProfile), true, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
