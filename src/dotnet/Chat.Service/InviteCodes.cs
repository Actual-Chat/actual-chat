using ActualChat.Chat.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public partial class InviteCodes : DbServiceBase<ChatDbContext>, IInviteCodes, IInviteCodesBackend
{
    private readonly ICommander _commander;
    private readonly IAuth _auth;
    private readonly IChatsBackend _chatsBackend;

    public InviteCodes(IServiceProvider services) : base(services)
    {
        _commander = Services.Commander();
        _auth = Services.GetRequiredService<IAuth>();
        _chatsBackend = Services.GetRequiredService<IChatsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<InviteCode>> Get(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        await _chatsBackend.AssertHasPermissions(session, chatId, ChatPermissions.Invite, cancellationToken).ConfigureAwait(false);

        return await Get(chatId, user.Id, cancellationToken).ConfigureAwait(false);
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

        await _chatsBackend.AssertHasPermissions(session, chatId, ChatPermissions.Invite, cancellationToken).ConfigureAwait(false);

        var inviteCode = new InviteCode {
            ChatId = chatId,
            CreatedBy = user.Id,
            ExpiresOn = Clocks.SystemClock.Now + TimeSpan.FromDays(7)
        };

        var generateInviteCodeCommand = new IInviteCodesBackend.GenerateCommand(inviteCode);
        return await _commander.Call(generateInviteCodeCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
