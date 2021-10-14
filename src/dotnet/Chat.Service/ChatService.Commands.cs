using ActualChat.Chat.Db;

namespace ActualChat.Chat;

public partial class ChatService
{
    [CommandHandler]
    public virtual async Task<Chat> CreateChat(
        ChatCommands.CreateChat command, CancellationToken cancellationToken)
    {
        var (session, title) = command;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            return null!; // Nothing to invalidate

        var user = await Auth.GetUser(session, cancellationToken);
        user.MustBeAuthenticated();

        await using var dbContext = await CreateCommandDbContext(cancellationToken);
        var now = Clocks.SystemClock.Now;
        var id = (ChatId)Ulid.NewUlid().ToString();
        var dbChat = new DbChat() {
            Id = id,
            Version = VersionGenerator.NextVersion(),
            Title = title,
            AuthorId = user.Id,
            CreatedAt = now,
            IsPublic = false,
            Owners = new List<DbChatOwner> { new() { ChatId = id, UserId = user.Id } },
        };
        dbContext.Add(dbChat);
        await dbContext.SaveChangesAsync(cancellationToken);

        var chat = dbChat.ToModel();
        context.Operation().Items.Set(chat);
        return chat;
    }

    [CommandHandler]
    public virtual async Task<ChatEntry> PostMessage(
        ChatCommands.PostMessage command, CancellationToken cancellationToken)
    {
        var (session, chatId, text) = command;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invChatEntry = context.Operation().Items.Get<ChatEntry>();
            _ = GetIdRange(chatId, default);
            InvalidateChatPages(chatId, invChatEntry.Id, false);
            return null!;
        }

        var user = await Auth.GetUser(session, cancellationToken);
        user.MustBeAuthenticated();
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Write, cancellationToken);

        var dbContext = await CreateCommandDbContext(cancellationToken);
        var now = Clocks.SystemClock.Now;
        var chatEntry = new ChatEntry(chatId, 0) {
            AuthorId = (string)user.Id,
            BeginsAt = now,
            EndsAt = now,
            Content = text,
            ContentType = ChatContentType.Text,
        };
        var dbChatEntry = await DbAddOrUpdate(dbContext, chatEntry, cancellationToken);
        chatEntry = dbChatEntry.ToModel();
        context.Operation().Items.Set(chatEntry);
        return chatEntry;
    }

    [CommandHandler]
    public virtual async Task<ChatEntry> CreateEntry(
        ChatCommands.CreateEntry command, CancellationToken cancellationToken)
    {
        var chatEntry = command.Entry;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invChatEntry = context.Operation().Items.Get<ChatEntry>();
            _ = GetIdRange(chatEntry.ChatId, default);
            InvalidateChatPages(chatEntry.ChatId, invChatEntry.Id, false);
            return null!;
        }

        await AssertHasPermissions(chatEntry.ChatId, chatEntry.AuthorId, ChatPermissions.Write, cancellationToken);

        await using var dbContext = await CreateCommandDbContext(cancellationToken);
        var dbChatEntry = await DbAddOrUpdate(dbContext, chatEntry, cancellationToken);
        chatEntry = dbChatEntry.ToModel();
        context.Operation().Items.Set(chatEntry);
        return chatEntry;
    }

    [CommandHandler]
    public virtual async Task<ChatEntry> UpdateEntry(
        ChatCommands.UpdateEntry command, CancellationToken cancellationToken)
    {
        var chatEntry = command.Entry;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invChatEntry = context.Operation().Items.Get<ChatEntry>();
            _ = GetIdRange(chatEntry.ChatId, default);
            InvalidateChatPages(chatEntry.ChatId, invChatEntry.Id, true);
            return null!;
        }

        await AssertHasPermissions(chatEntry.ChatId, chatEntry.AuthorId, ChatPermissions.Write, cancellationToken);

        await using var dbContext = await CreateCommandDbContext(cancellationToken);
        var dbChatEntry = await DbAddOrUpdate(dbContext, chatEntry, cancellationToken);
        chatEntry = dbChatEntry.ToModel();
        context.Operation().Items.Set(chatEntry);
        return chatEntry;
    }
}
