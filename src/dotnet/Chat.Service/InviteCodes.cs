using ActualChat.Chat.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class InviteCodes : DbServiceBase<ChatDbContext>, IInviteCodes
{
    private readonly ICommander _commander;
    private readonly IAuth _auth;
    private readonly IInviteCodesBackend _backend;
    private readonly IChatsBackend _chatsBackend;

    public InviteCodes(IServiceProvider services, ICommander commander, IAuth auth, IInviteCodesBackend backend, IChatsBackend chatsBackend) : base(services)
    {
        _commander = commander;
        _auth = auth;
        _backend = backend;
        _chatsBackend = chatsBackend;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<InviteCode>> Get(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        await AssertCanInvite(session, chatId, cancellationToken).ConfigureAwait(false);

        return await _backend.Get(chatId, user.Id, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<InviteCode> Generate(
        IInviteCodes.GenerateCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId) = command;
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        await AssertCanInvite(session, chatId, cancellationToken).ConfigureAwait(false);

        var inviteCode = new InviteCode {
            ChatId = chatId,
            CreatedBy = user.Id,
            ExpiresOn = Clocks.SystemClock.Now + TimeSpan.FromDays(7)
        };

        var generateInviteCodeCommand = new IInviteCodesBackend.GenerateCommand(inviteCode);
        return await _commander.Call(generateInviteCodeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<InviteCodeUseResult> UseInviteCode(IInviteCodes.UseInviteCodeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, inviteCodeValue) = command;

        var inviteCode = await FindInviteCode(inviteCodeValue, cancellationToken).ConfigureAwait(false);
        if (!CheckIfValid(inviteCode))
            return new InviteCodeUseResult {IsValid = false};
        var chatId = (string)inviteCode!.ChatId;
        var chat = await _chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return new InviteCodeUseResult {IsValid = false};

        var useCommand = new IInviteCodesBackend.UseInviteCodeCommand(session, inviteCode);
        await _commander.Call(useCommand, true, cancellationToken).ConfigureAwait(false);
        return new InviteCodeUseResult {IsValid = true, ChatId = chat.Id };
    }

    // Private methods

    private async Task AssertCanInvite(Session session, string chatId, CancellationToken cancellationToken)
    {
        var permissions = await _chatsBackend.GetPermissions(session, chatId, cancellationToken).ConfigureAwait(false);
        permissions.AssertHasPermissions(ChatPermissions.Invite);
    }

    private Task<InviteCode?> FindInviteCode(string inviteCodeValue, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(inviteCodeValue))
            throw new ArgumentException("Value cannot be null or empty.", nameof(inviteCodeValue));
        return _backend.GetByValue(inviteCodeValue, cancellationToken);
    }

    private bool CheckIfValid(InviteCode? inviteCode)
        => inviteCode != null && inviteCode.State == InviteCodeState.Active && inviteCode.ExpiresOn > Clocks.SystemClock.UtcNow;
}
