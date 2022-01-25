using System.Security;
using ActualChat.Chat.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class Chats : DbServiceBase<ChatDbContext>, IChats
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;

    private readonly ICommander _commander;
    private readonly IAuth _auth;
    private readonly IChatAuthors _chatAuthors;
    private readonly IChatAuthorsBackend _chatAuthorsBackend;
    private readonly IChatsBackend _chatsBackend;
    private readonly IInviteCodesBackend _inviteCodesBackend;

    public Chats(IServiceProvider services) : base(services)
    {
        _commander = Services.Commander();
        _auth = Services.GetRequiredService<IAuth>();
        _chatAuthors = Services.GetRequiredService<IChatAuthors>();
        _chatAuthorsBackend = Services.GetRequiredService<IChatAuthorsBackend>();
        _chatsBackend = Services.GetRequiredService<IChatsBackend>();
        _inviteCodesBackend = Services.GetRequiredService<IInviteCodesBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken)
    {
        var canRead = await _chatsBackend.CheckHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (!canRead)
            return null;
        return await _chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Chat[]> GetChats(Session session, CancellationToken cancellationToken)
    {
        var chatIds = await _chatAuthors.GetChatIds(session, cancellationToken).ConfigureAwait(false);
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated) {
            var ownedChatIds = await _chatsBackend.GetOwnedChatIds(user.Id, cancellationToken).ConfigureAwait(false);
            chatIds = chatIds.Union(ownedChatIds, StringComparer.Ordinal).ToArray();
        }

        var chatTasks = await Task
            .WhenAll(chatIds.Select(id => Get(session, id, cancellationToken)))
            .ConfigureAwait(false);
        return chatTasks.Where(c => c != null).Select(c => c!).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<InviteCodeCheckResult> CheckInviteCode(Session session, string inviteCode, CancellationToken cancellationToken)
    {
        var chatId = await GetChatIdFromInviteCode(inviteCode, cancellationToken).ConfigureAwait(false);
        if (chatId.IsNullOrEmpty())
            return new InviteCodeCheckResult {IsValid = false};
        var chat = await _chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return new InviteCodeCheckResult {IsValid = false};
        return new InviteCodeCheckResult {IsValid = true, ChatTitle = chat.Title};
    }

    // [ComputeMethod]
    public virtual async Task<ChatTile> GetTile(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
    {
        await _chatsBackend.AssertHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _chatsBackend.GetTile(chatId, entryType, idTileRange, false, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetEntryCount(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long>? idTileRange,
        CancellationToken cancellationToken)
    {
        await _chatsBackend.AssertHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _chatsBackend.GetEntryCount(chatId, entryType, idTileRange, false, cancellationToken).ConfigureAwait(false);
    }

    // Note that it returns (firstId, lastId + 1) range!
    // [ComputeMethod]
    public virtual async Task<Range<long>> GetIdRange(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
    {
        await _chatsBackend.AssertHasPermissions(session, chatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _chatsBackend.GetIdRange(chatId, entryType, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatPermissions> GetPermissions(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        var chatPrincipalId = await _chatAuthors.GetChatPrincipalId(session, chatId, cancellationToken).ConfigureAwait(false);
        return await _chatsBackend.GetPermissions(chatId, chatPrincipalId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Chat> CreateChat(IChats.CreateChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, title) = command;
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        var chat = new Chat() {
            Title = title,
            IsPublic = command.IsPublic,
            OwnerIds = ImmutableArray.Create(user.Id),
        };
        var createChatCommand = new IChatsBackend.CreateChatCommand(chat);
        return await _commander.Call(createChatCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Unit> UpdateChat(IChats.UpdateChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chat) = command;
        await _chatsBackend.AssertHasPermissions(session, chat.Id, ChatPermissions.Admin, cancellationToken).ConfigureAwait(false);

        var updateChatCommand = new IChatsBackend.UpdateChatCommand(chat);
        return await _commander.Call(updateChatCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Unit> JoinPublicChat(IChats.JoinPublicChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId) = command;

        var chat = await _chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        var enoughPermissions = chat != null && chat.IsPublic;
        if (!enoughPermissions)
            throw new InvalidOperationException("Invalid command");

        await JoinChat(session, chatId, cancellationToken).ConfigureAwait(false);
        return Unit.Default;
    }

    // [CommandHandler]
    public virtual async Task<string> JoinWithInviteCode(IChats.JoinWithInviteCodeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, inviteCode) = command;
        var chatId = await GetChatIdFromInviteCode(inviteCode, cancellationToken).ConfigureAwait(false);
        if (chatId.IsNullOrEmpty())
            throw new InvalidOperationException("Invitation code is expired or invalid");

        var chat = await _chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        var enoughPermissions = chat != null;
        if (!enoughPermissions)
            throw new InvalidOperationException("Invalid command");

        await JoinChat(session, chatId, cancellationToken).ConfigureAwait(false);
        return chatId;
    }

    // [CommandHandler]
    public virtual async Task<ChatEntry> CreateTextEntry(
        IChats.CreateTextEntryCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId, text) = command;
        // NOTE(AY): Temp. commented this out, coz it confuses lots of people who're trying to post in anonymous mode
        // await _chatsBackend.AssertHasPermissions(session, chatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);
        await _chatsBackend.AssertHasPermissions(session, chatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);
        var author = await _chatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);

        var chatEntry = new ChatEntry() {
            ChatId = chatId,
            AuthorId = author.Id,
            Content = text,
            Type = ChatEntryType.Text,
        };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry);
        return await _commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task RemoveTextEntry(IChats.RemoveTextEntryCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, entryId) = command;
        await _chatsBackend.AssertHasPermissions(session, chatId, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);
        var author = await _chatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);

        var idTile = IdTileStack.FirstLayer.GetTile(entryId);
        var tile = await GetTile(session, chatId, ChatEntryType.Text, idTile.Range, cancellationToken).ConfigureAwait(false);
        var chatEntry = tile.Entries.Single(e => e.Id == entryId);
        if (chatEntry.AuthorId != author.Id)
            throw new SecurityException("You can delete only your own messages.");

        chatEntry = chatEntry with { IsRemoved = true };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry);
        await _commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    private async Task<string> GetChatIdFromInviteCode(string inviteCodeValue, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(inviteCodeValue))
            throw new ArgumentException("Value cannot be null or empty.", nameof(inviteCodeValue));

        var obj = await _inviteCodesBackend.GetByValue(inviteCodeValue, cancellationToken).ConfigureAwait(false);
        if (obj == null || obj.State != InviteCodeState.Active || obj.ExpiresOn <= Clocks.SystemClock.UtcNow)
            return "";
        return obj.ChatId;
    }

    private async Task JoinChat(Session session, string chatId, CancellationToken cancellationToken)
        => await _chatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);
}
