using ActualChat.Chat.ML;
using ActualChat.Contacts;

namespace ActualChat.Chat;

/// <summary>
/// Frontend service for managing chat threads (nested conversations within a chat).
/// </summary>
public class ChatThreads(IServiceProvider services) : IChatThreads
{
    private IChatThreadsBackend Backend => field ??= services.GetRequiredService<IChatThreadsBackend>();
    private IChats Chats => field ??= services.GetRequiredService<IChats>();
    private IChatsBackend ChatsBackend => field ??= services.GetRequiredService<IChatsBackend>();
    private IPlaces Places => field ??= services.GetRequiredService<IPlaces>();
    private IAccounts Accounts => field ??= services.GetRequiredService<IAccounts>();
    private IContactsBackend ContactsBackend => field ??= services.GetRequiredService<IContactsBackend>();
    private ICommander Commander => field ??= services.GetRequiredService<ICommander>();
    private IThreadInsightExtractor ThreadInsightExtractor => field ??= services.GetRequiredService<IThreadInsightExtractor>();
    private ILogger Log => field ??= services.LogFor(GetType());

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
        PlaceId? parentPlaceId,
        CancellationToken cancellationToken)
    {
        if (parentPlaceId is not null)
            await Places.Get(session, parentPlaceId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the place
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuest)
            return [];

        var placeContacts = await ContactsBackend
            .ListIds(account.Id, parentPlaceId, cancellationToken)
            .ConfigureAwait(false);
        Func<ChatId, bool> chatFilter = parentPlaceId is not null
            ? c => c.Kind is ChatKind.Place
            : c => c.Kind is ChatKind.Group or ChatKind.Peer;
        var placeChatIds = placeContacts.Select(c => c.ChatId).Where(chatFilter).ToHashSet();
        var placeTheadIds = await ContactsBackend
            .ListThreadIdsForPlace(account.Id, parentPlaceId, cancellationToken)
            .ConfigureAwait(false);
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
        var range = await ChatsBackend.GetLidRange(threadChatId, false, cancellationToken).ConfigureAwait(false);
        var entries = ChatsBackend.ReadEntries(threadChatId, range, false, cancellationToken);
        var entryCount = 0;
        var attachmentList = new List<ChatEntryAttachment>();
        const int entryCountLimit = 30;
        await foreach (var chatEntry in entries.ConfigureAwait(false)) {
            entryCount++;
            if (chatEntry.IsSystemEntry)
                continue;

            messageCount++;
            var authorId = chatEntry.AuthorId;
            if (authorIds.Add(authorId) && topAuthorIds.Count < displayAuthorsLimit)
                topAuthorIds.Add(authorId);
            if (chatEntry.Attachments.Length > 0) {
                attachmentList.AddRange(chatEntry.Attachments);
            }
            if (entryCount >= entryCountLimit) //NOTE(DF): count
                break;
        }
        // NOTE(DF): When total entry count is above the limit, we don't count them precisely.
        if (entryCount >= entryCountLimit)
            messageCount = (int)range.End - 1;
        return new ThreadStat(messageCount, topAuthorIds.ToArray(), authorIds.Count, attachmentList.ToArray());
    }

    // Non-computed
    public virtual async Task<(string, string)> SuggestThreadTitle(
        Session session,
        ChatId parentChatId,
        ChatEntryId[] entryIds,
        CancellationToken cancellationToken)
    {
        await Chats.Get(session, parentChatId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the chat
        var textEntries = new List<ChatEntrySlim>();
        foreach (var chatEntryId in entryIds) {
            var chatEntry = await Chats.GetEntry(session, chatEntryId, cancellationToken).ConfigureAwait(false);
            if (chatEntry is not null)
                textEntries.Add(new ChatEntrySlim(chatEntry));
        }
        if (textEntries.Count is 0)
            return ("", "");

        try {
            var insight = await ThreadInsightExtractor.GetInsight(textEntries, cancellationToken).ConfigureAwait(false);
            return (insight.Title, insight.Description);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to suggest thread title");
            return ("", "");
        }
    }

    // [CommandHandler]
    public virtual async Task<Chat> OnStart(ChatThreads_Start command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var parentChatId = command.ParentChatId;
        var title = command.Title;
        var description = command.Description;
        var parentChat = await Chats.Get(session, parentChatId, cancellationToken).Require().ConfigureAwait(false);
        parentChat.Rules.Permissions.Require(ChatPermissions.Write);
        var ownerId = parentChat.Rules.Account!.Id;
        if (parentChatId is PeerChatId peerChatId) {
            var peerUserId = peerChatId.AnotherUserId(ownerId);
            var peerContactId = ContactId.NewUser(peerUserId, ownerId);
            var peerContact = await ContactsBackend.Get(peerUserId, peerContactId, cancellationToken)
                .ConfigureAwait(false);
            if (!peerContact.IsRegular)
                throw StandardError.Constraint("Threads can be started only after this user adds you to their contacts or replies.");
        }

        var isFirst = true;
        Chat? threadChat = null;
        foreach (var chatEntryId in command.EntryIds.OrderBy(c => c.LocalId)) {
            var textEntry = await Chats.GetEntry(session, chatEntryId, cancellationToken).ConfigureAwait(false);
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
                var textEntryId1 = ChatEntryId.New(chatId, 0);
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
                    ? new ChatEntryDiff { IsThreadStart = true }
                    : new ChatEntryDiff { IsThread = true };
                var upsertEntryCommand = new ChatsBackend_ChangeEntry(
                    chatEntryId,
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
            return default; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var threadChatId = command.ThreadChatId;
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
