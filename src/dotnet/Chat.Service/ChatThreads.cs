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

        var chatThread = await Commander.Call(new ChatThreadsBackend_Start(parentChat.Id, title), cancellationToken).ConfigureAwait(false);
        var chatId = chatThread.Id;
        var chatChange = Change.Create(new ChatDiff {
            Title = chatThread.Title,
        });
        var chat = await Commander.Call(new ChatsBackend_Change(chatId, null, chatChange), cancellationToken).ConfigureAwait(false);

        foreach (var textEntryId in command.Entries.OrderBy(c => c.LocalId)) {
            var textEntry = await Chats.GetEntry(session, textEntryId, cancellationToken).ConfigureAwait(false);
            if (textEntry is null || textEntry.IsRemoved)
                continue;

            // Create
            var textEntryId1 = new TextEntryId(chatId, 0, AssumeValid.Option);
            var upsertEntryCommand = new ChatsBackend_ChangeEntry(
                textEntryId1,
                null,
                Change.Create(new ChatEntryDiff {
                    AuthorId = textEntry.AuthorId,
                    Content = textEntry.Content,
                    Attachments = textEntry.Attachments.IsEmpty ? null : textEntry.Attachments,
                }));
            var textEntry1 = await Commander.Call(upsertEntryCommand, cancellationToken).ConfigureAwait(false);
        }

        return chatThread;
    }
}
