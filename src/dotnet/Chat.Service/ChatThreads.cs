namespace ActualChat.Chat;

public class ChatThreads(IServiceProvider services) : IChatThreads
{
    [field: AllowNull, MaybeNull]
    private IChatThreadsBackend Backend => field ??= services.GetRequiredService<IChatThreadsBackend>();
    [field: AllowNull, MaybeNull]
    private IChats Chats => field ??= services.GetRequiredService<IChats>();
    [field: AllowNull, MaybeNull]
    private ICommander Commander => field ??= services.GetRequiredService<ICommander>();

    public virtual async Task<ApiArray<ChatId>> ListIds(
        Session session,
        ChatId parentChatId,
        CancellationToken cancellationToken)
    {
        await Chats.Get(session, parentChatId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the chat
        return await Backend.ListIds(parentChatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ChatThread> OnStart(ChatThreads_Start command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!; //

        var session = command.Session;
        var parentChatId = command.ParentChatId;
        var title = command.Title;
        var parentChat = await Chats.Get(session, parentChatId, cancellationToken).Require().ConfigureAwait(false);
        parentChat.Rules.Permissions.Require(ChatPermissions.Write);
        var ownerId = parentChat.Rules.Account.Id;

        var isFirst = true;
        var chatId = ChatId.None;
        ChatThread chatThread = null!;
        foreach (var textEntryId in command.Entries.OrderBy(c => c.LocalId)) {
            var textEntry = await Chats.GetEntry(session, textEntryId, cancellationToken).ConfigureAwait(false);
            if (textEntry is null || textEntry.IsRemoved)
                continue;

            if (isFirst) {
                var threadId = textEntry.LocalId; // Start Entry Id
                chatId = parentChatId.CreateThreadId(threadId);
                chatThread = await Commander.Call(new ChatThreadsBackend_Start(chatId, title), cancellationToken).ConfigureAwait(false);
                var chatChange = Change.Create(new ChatDiff {
                    Title = chatThread.Title,
                });
                var chat = await Commander.Call(new ChatsBackend_Change(chatId, null, chatChange, OwnerId:ownerId), cancellationToken).ConfigureAwait(false);
            }

            // Create thread chat entry.
            {
                var textEntryId1 = new TextEntryId(chatId, 0, AssumeValid.Option);
                var upsertEntryCommand = new ChatsBackend_ChangeEntry(
                    textEntryId1,
                    null,
                    Change.Create(new ChatEntryDiff {
                        BeginsAt = textEntry.BeginsAt,
                        AuthorId = textEntry.AuthorId,
                        Content = textEntry.Content,
                        Attachments = textEntry.Attachments.IsEmpty ? null : textEntry.Attachments,
                    }));
                await Commander.Call(upsertEntryCommand, cancellationToken).ConfigureAwait(false);
            }
            {
                // Mark source entry as thread entry.
                var diff = isFirst
                    ? new ChatEntryDiff { IsThreadStartEntry = true }
                    : new ChatEntryDiff { IsThreadEntry = true };
                var upsertEntryCommand = new ChatsBackend_ChangeEntry(
                    textEntryId,
                    null,
                    Change.Update(diff));
                await Commander.Call(upsertEntryCommand, cancellationToken).ConfigureAwait(false);
                isFirst = false;
            }
        }

        return chatThread;
    }
}
