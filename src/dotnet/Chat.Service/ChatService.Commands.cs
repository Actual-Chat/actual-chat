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

        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        await using var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
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
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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
            InvalidateChatPages(chatId, invChatEntry.Id, false);
            _ = GetIdRange(chatId, default); // We invalidate min-max Id range at last
            return null!;
        }

        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        var chatEntry = new ChatEntry(chatId, 0) {
            AuthorId = (string)user.Id,
            BeginsAt = now,
            EndsAt = now,
            Content = text,
            Type = ChatEntryType.Text,
        };
        var dbChatEntry = await DbAddOrUpdate(dbContext, chatEntry, cancellationToken).ConfigureAwait(false);
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
            InvalidateChatPages(chatEntry.ChatId, invChatEntry.Id, false);
            _ = GetIdRange(chatEntry.ChatId, default); // We invalidate min-max Id range at last
            return null!;
        }

        await AssertHasPermissions(chatEntry.ChatId, chatEntry.AuthorId, ChatPermissions.Write, cancellationToken)
            .ConfigureAwait(false);

        await using var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        var dbChatEntry = await DbAddOrUpdate(dbContext, chatEntry, cancellationToken).ConfigureAwait(false);
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
            InvalidateChatPages(chatEntry.ChatId, invChatEntry.Id, true);
            // No need to invalidate GetIdRange here
            return null!;
        }

        await AssertHasPermissions(chatEntry.ChatId, chatEntry.AuthorId, ChatPermissions.Write, cancellationToken)
            .ConfigureAwait(false);

        await using var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        var dbChatEntry = await DbAddOrUpdate(dbContext, chatEntry, cancellationToken).ConfigureAwait(false);
        chatEntry = dbChatEntry.ToModel();
        context.Operation().Items.Set(chatEntry);
        return chatEntry;
    }
}
