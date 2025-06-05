using ActualChat.Chat.ML;
using ActualChat.Contacts;
using ActualChat.Users;

namespace ActualChat.Chat;

public class ChatThreads(IServiceProvider services) : IChatThreads
{
    [field: AllowNull, MaybeNull]
    private IChatThreadsBackend Backend => field ??= services.GetRequiredService<IChatThreadsBackend>();
    [field: AllowNull, MaybeNull]
    private IChats Chats => field ??= services.GetRequiredService<IChats>();
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= services.GetRequiredService<IChatsBackend>();
    [field: AllowNull, MaybeNull]
    private IPlaces Places => field ??= services.GetRequiredService<IPlaces>();
    [field: AllowNull, MaybeNull]
    private IAccounts Accounts => field ??= services.GetRequiredService<IAccounts>();
    [field: AllowNull, MaybeNull]
    private IContactsBackend ContactsBackend => field ??= services.GetRequiredService<IContactsBackend>();
    [field: AllowNull, MaybeNull]
    private ICommander Commander => field ??= services.GetRequiredService<ICommander>();
    [field: AllowNull, MaybeNull]
    private IThreadInsightExtractor ThreadInsightExtractor => field ??= services.GetRequiredService<IThreadInsightExtractor>();

    // [ComputeMethod]
    public virtual async Task<ThreadChatId[]> ListIdsForChat(
        Session session,
        ChatId parentChatId,
        CancellationToken cancellationToken)
    {
        await Chats.Get(session, parentChatId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the chat
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuest)
            return [];

        return await ContactsBackend.ListThreadIdsForChat(account.Id, parentChatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ThreadChatId[]> ListIdsForPlace(
        Session session,
        PlaceId parentPlaceId,
        CancellationToken cancellationToken)
    {
        await Places.Get(session, parentPlaceId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the place
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuest)
            return [];

        var placeContacts = await ContactsBackend.ListIds(account.Id, parentPlaceId, cancellationToken).ConfigureAwait(false);
        var placeChatIds = placeContacts.Select(c => c.ChatId).Where(c => c.Kind is ChatKind.Place).ToHashSet();
        var placeTheadIds = await ContactsBackend.ListThreadIdsForPlace(account.Id, parentPlaceId, cancellationToken).ConfigureAwait(false);
        return placeTheadIds.Where(c => placeChatIds.Contains(c.GetOutermostParent())).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<bool> GetThreadFollowStatus(Session session, ThreadChatId threadChatId, CancellationToken cancellationToken)
    {
        if (!threadChatId.IsThread())
            throw new ArgumentOutOfRangeException(nameof(threadChatId));

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuest)
            return false;

        var contactId = ContactId.NewAny(account.Id, threadChatId);
        var threadContact = await ContactsBackend.GetThreadContact(account.Id, contactId, cancellationToken).ConfigureAwait(false);
        return threadContact is not null;
    }

    // [ComputeMethod]
    public virtual async Task<ThreadStat> GetThreadStat(
        Session session,
        ThreadChatId threadChatId,
        CancellationToken cancellationToken)
    {
        await Chats.Get(session, threadChatId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the chat
        return await GetThreadStatInternal(threadChatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Author?> GetThreadCreator(
        Session session,
        ThreadChatId chatId,
        CancellationToken cancellationToken)
    {
        if (!chatId.IsThread(out var threadChatId))
            throw new ArgumentOutOfRangeException(nameof(chatId));

        await Chats.Get(session, threadChatId.ParentChatId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the parent chat
        return await Backend.GetThreadCreator(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    protected virtual async Task<ThreadStat> GetThreadStatInternal(ThreadChatId threadChatId, CancellationToken cancellationToken)
    {
        const int displayAuthorsLimit = 3;
        var messageCount = 0;
        var topAuthorIds = new List<AuthorId>();
        var authorIds = new HashSet<AuthorId>();
        var range = await ChatsBackend.GetIdRange(threadChatId, ChatEntryKind.Text, false, cancellationToken).ConfigureAwait(false);
        var entries = ChatsBackend.ReadEntries(threadChatId, ChatEntryKind.Text, range, false, cancellationToken);
        var entryCount = 0;
        const int entryCountLimit = 30;
        await foreach (var chatEntry in entries.ConfigureAwait(false)) {
            entryCount++;
            if (chatEntry.IsSystemEntry)
                continue;

            messageCount++;
            var authorId = chatEntry.AuthorId;
            if (authorIds.Add(authorId) && topAuthorIds.Count < displayAuthorsLimit)
                topAuthorIds.Add(authorId);
            if (entryCount >= entryCountLimit) //NOTE(DF): count
                break;
        }
        // NOTE(DF): When total entry count is above the limit, we don't count them precisely.
        if (entryCount >= entryCountLimit)
            messageCount = (int)range.End - 1;
        return new ThreadStat(messageCount, topAuthorIds.ToArray(), authorIds.Count);
    }

    // Non-computed
    public virtual async Task<(string, string)> SuggestThreadTitle(
        Session session,
        ChatId parentChatId,
        TextEntryId[] entryIds,
        CancellationToken cancellationToken)
    {
        await Chats.Get(session, parentChatId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the chat
        var textEntries = new List<TextEntry>();
        foreach (var textEntryId in entryIds) {
            var chatEntry = await Chats.GetEntry(session, textEntryId, cancellationToken).ConfigureAwait(false);
            if (chatEntry is not null)
                textEntries.Add(new TextEntry(chatEntry));
        }
        if (textEntries.Count is 0)
            return ("", "");

        var insight = await ThreadInsightExtractor.GetInsight(textEntries, cancellationToken).ConfigureAwait(false);
        return (insight.Title, insight.Description);
    }

    // [CommandHandler]
    public virtual async Task<Chat> OnStart(ChatThreads_Start command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!; //

        var session = command.Session;
        var parentChatId = command.ParentChatId;
        var title = command.Title;
        var description = command.Description;
        var parentChat = await Chats.Get(session, parentChatId, cancellationToken).Require().ConfigureAwait(false);
        parentChat.Rules.Permissions.Require(ChatPermissions.Write);
        var ownerId = parentChat.Rules.Account!.Id;

        var isFirst = true;
        Chat? threadChat = null;
        foreach (var textEntryId in command.EntryIds.OrderBy(c => c.LocalId)) {
            var textEntry = await Chats.GetEntry(session, textEntryId, cancellationToken).ConfigureAwait(false);
            if (textEntry is null || textEntry.IsRemoved)
                continue;

            if (threadChat is null) {
                var threadId = textEntry.LocalId; // Start Entry Id
                var threadChatId = parentChatId.CreateThreadId(threadId);
                var chatChange = Change.Create(new ChatDiff {
                    Title = title,
                    Description = description,
                    IsPublic = false,
                });
                threadChat = await Commander.Call(new ChatsBackend_Change(threadChatId, null, chatChange, OwnerId:ownerId), cancellationToken).ConfigureAwait(false);
            }
            var chatId = threadChat.Id;

            // Create a thread chat entry
            {
                var textEntryId1 = TextEntryId.New(chatId, 0);
                var textEntryAuthorId = AuthorsBackend.Remap(textEntry.AuthorId, chatId);
                var upsertEntryCommand = new ChatsBackend_ChangeEntry(
                    textEntryId1,
                    null,
                    Change.Create(new ChatEntryDiff {
                        BeginsAt = textEntry.BeginsAt,
                        AuthorId = textEntryAuthorId,
                        Content = textEntry.Content,
                        Attachments = textEntry.Attachments.Length is 0 ? null : textEntry.Attachments,
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

        if (threadChat is null)
            throw StandardError.Internal("Thread chat has not been created.");
        return threadChat;
    }

    // [CommandHandler]
    public virtual async Task<Unit> OnToggleThreadFollowStatus(
        ChatThreads_ToggleThreadFollowStatus command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default;

        var (session, threadChatId) = command;
        if (!threadChatId.IsThread())
            throw new ArgumentOutOfRangeException(nameof(threadChatId));

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuest)
            throw new ArgumentOutOfRangeException(nameof(session));

        var contactId = ContactId.NewAny(account.Id, threadChatId);
        var threadContact = await ContactsBackend.GetThreadContact(account.Id, contactId, cancellationToken).ConfigureAwait(false);
        var change = threadContact is null ? Change.Create(new ThreadContact(contactId)) : Change.Remove<ThreadContact>();
        var toggleCommand = new ContactsBackend_ChangeThreadContact(contactId, null, change);
        await Commander.Call(toggleCommand, cancellationToken).ConfigureAwait(false);
        return Unit.Default;
    }
}
