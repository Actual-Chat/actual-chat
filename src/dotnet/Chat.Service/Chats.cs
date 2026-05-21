using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.Transcription;

namespace ActualChat.Chat;

/// <summary>
/// Frontend service for chat operations with session-based access control.
/// </summary>
public partial class Chats(IServiceProvider services) : IChats
{
    public static readonly TileStack<long> ServerIdTileStack = Constants.Chat.ServerIdTileStack;
    public static readonly TileStack<long> ViewIdTileStack = Constants.Chat.ViewIdTileStack;

    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private IAvatars Avatars { get; } = services.GetRequiredService<IAvatars>();
    private IPlaces Places => field ??= services.GetRequiredService<IPlaces>(); // Lazy resolving to prevent cyclic dependency
    private IConversationsBackend ConversationsBackend { get; } = services.GetRequiredService<IConversationsBackend>();

    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IChatPositionsBackend ChatPositionsBackend { get; } = services.GetRequiredService<IChatPositionsBackend>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private IRolesBackend RolesBackend { get; } = services.GetRequiredService<IRolesBackend>();
    private IChatsBackend Backend { get; } = services.GetRequiredService<IChatsBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
        = services.KeyedFactory<IBackendChatMarkupHub, ChatId>();

    private ICommander Commander { get; } = services.Commander();
    private HostInfo HostInfo => field ??= services.HostInfo();
    private ILogger Log { get; } = services.LogFor<Chats>();

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Backend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chatId.Kind == ChatKind.Peer) {
            var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
            if (!ContactId.TryParse(ContactId.Format(account.Id, chatId), out var contactId))
                return null;

            var contact = await ContactsBackend.Get(account.Id, contactId, cancellationToken).ConfigureAwait(false);
            var peerAccount = contact.Account;
            if (peerAccount == null)
                return null; // No peer account

            chat ??= new Chat(chatId);
            var avatar = peerAccount.Avatar;
            var peerName = contact.PreferredPeerName ?? avatar.Name;
            chat = chat with {
                Title = peerName,
                Picture = avatar.Media,
            };
        }
        else {
            if (chat == null)
                return null;

            if (chatId is PlaceChatId { IsRoot: false } placeChatId) {
                var place = await Places.Get(session, placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
                if (place == null)
                    return null;
            }
        }

        var rules = await GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanRead())
            return null;

        chat = chat with { Rules = rules };

        return chat;
    }

    // [ComputeMethod]
    public virtual async Task<ChatTile> GetTile(
        Session session,
        ChatId chatId,
        Range<long> lidTileRange,
        CancellationToken cancellationToken)
    {
        await RequireCanRead(session, chatId, cancellationToken).ConfigureAwait(false);
        return await Backend.GetTile(chatId, lidTileRange, false, cancellationToken).ConfigureAwait(false);
    }

    // Legacy compat: old clients send ChatEntryKind parameter
    [Obsolete("2026.03: Use GetTile without entryKind")]
    public virtual Task<ChatTile> GetTile(
        Session session, ChatId chatId, int entryKind, Range<long> lidTileRange, CancellationToken cancellationToken)
        => GetTile(session, chatId, lidTileRange, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<ChatContentTile> GetChatContentTile(
        Session session,
        ChatId chatId,
        ChatContentKind kindMask,
        Range<long> entryLidTileRange,
        CancellationToken cancellationToken)
    {
        await RequireCanRead(session, chatId, cancellationToken).ConfigureAwait(false);
        var tile = await Backend.GetChatContentTile(chatId, entryLidTileRange, cancellationToken).ConfigureAwait(false);
        if (kindMask is ChatContentKind.All || tile.IsEmpty)
            return tile;

        var items = tile.Items.Where(x => (x.Kind & kindMask) != 0).ToArray();
        return new ChatContentTile(entryLidTileRange, kindMask, items);
    }

    // [ComputeMethod]
    public virtual async Task<ChatContentItem[]> ListChatContent(
        Session session,
        ChatId chatId,
        ChatContentKind kindMask,
        CancellationToken cancellationToken)
    {
        await RequireCanRead(session, chatId, cancellationToken).ConfigureAwait(false);
        var items = await Backend.ListChatContent(chatId, cancellationToken).ConfigureAwait(false);
        return kindMask is ChatContentKind.All
            ? items
            : items.Where(x => (x.Kind & kindMask) != 0).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<ChatRangeMeta> GetChatRangeMeta(
        Session session,
        ChatId chatId,
        long idTileStart,
        CancellationToken cancellationToken)
    {
        await RequireCanRead(session, chatId, cancellationToken).ConfigureAwait(false);
        return await Backend.GetChatRangeMeta(chatId, idTileStart, cancellationToken).ConfigureAwait(false);
    }

    // Note that it returns (firstId, lastId + 1) range!
    // [ComputeMethod]
    public virtual async Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        await RequireCanRead(session, chatId, cancellationToken).ConfigureAwait(false);
        return await Backend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
    }

    // Legacy compat: old clients send ChatEntryKind parameter
    [Obsolete("2026.03: Use GetIdRange without entryKind")]
    public virtual Task<Range<long>> GetIdRange(
        Session session, ChatId chatId, int entryKind, CancellationToken cancellationToken)
        => GetIdRange(session, chatId, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<AuthorRules> GetRules(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var principalId = await GetOwnPrincipalId(session, chatId, cancellationToken).ConfigureAwait(false);
        var rules = await Backend.GetRules(chatId, principalId, cancellationToken).ConfigureAwait(false);
        if (chatId.IsThread()) {
            var permissions = rules.Permissions;
            if (permissions.HasFlag(ChatPermissions.Write))
                permissions |= ChatPermissions.EditProperties;
            rules = rules with {
                Permissions = permissions,
            };
        }
        return rules;
    }

    // [ComputeMethod]
    public virtual async Task<ChatNews?> GetNews(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        if (!await CanRead(session, chatId, cancellationToken).ConfigureAwait(false))
            return null;

        return await Backend.GetNews(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Author[]> ListMentionableAuthors(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        await RequireCanRead(session, chatId, cancellationToken).ConfigureAwait(false);
        var authorIds = await AuthorsBackend.ListAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var authors = await authorIds
            .Select(id => Authors.Get(session, chatId, id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return authors
            .SkipNullItems()
            .OrderBy(a => a.Avatar.Name)
            .ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<ChatCopyState?> GetChatCopyState(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        return await Backend.GetChatCopyState(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatId?> GetForwardChatReplacement(
        Session session,
        ChatId sourceChatId,
        CancellationToken cancellationToken)
    {
        var chatId = await Backend.GetForwardChatReplacement(sourceChatId, cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return null;

        var chat = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        return chat?.Id;
    }

    // [ComputeMethod]
    public virtual async Task<ReadPositionsStat> GetReadPositionsStat(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return new ReadPositionsStat(chatId, long.MaxValue, []);
        // Stat is the same for all chat members, but we had to check read permissions first.
        return await GetReadPositionsStatInternal(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<bool> IsEntryReadByMentionedUser(Session session, ChatEntryId chatEntryId, MentionRef mentionId, CancellationToken cancellationToken)
    {
        var chatId = chatEntryId.ChatId;
        var chat = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return false;

        var chatEntry = await this.GetEntry(session, chatEntryId, cancellationToken).ConfigureAwait(false);
        if (chatEntry is null)
            return false;

        var mentionIds = await GetMentionIds().ConfigureAwait(false);
        if (!mentionIds.Contains(mentionId)) // Validate given mention id is used in the chat entry.
            return false;

        var userId = mentionId.Target as UserId;
        if (mentionId.Target is AuthorId authorId) {
            var author = await AuthorsBackend.Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
            if (author is not null)
                userId = author.UserId;
        }
        if (userId is null)
            return false;
        if (userId == chat.Rules.Account?.Id)
            return true; // Mention refers to the chat entry author.

        var readPosition = await ChatPositionsBackend.Get(userId, chatId, ChatPositionKind.Read, cancellationToken).ConfigureAwait(false);
        var hasRead = readPosition.EntryLid >= chatEntry.LocalId;
        // TODO: Do not track dependency after resulting to true.
        return hasRead;

        async Task<HashSet<MentionRef>> GetMentionIds()
        {
            var chatMarkupHub = ChatMarkupHubFactory[chatEntry.ChatId];
            var markup = await chatMarkupHub.GetMarkup(chatEntry, MarkupConsumer.Notification, cancellationToken).ConfigureAwait(false);
            return MentionExtractor.Instance.GetMentionIds(markup);
        }
    }

    // [CommandHandler]
    public virtual async Task<Chat> OnChange(Chats_Change command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (session, chatId, expectedVersion, change) = command;
        var chat = chatId is null
            ? null
            : await Get(session, chatId, cancellationToken).ConfigureAwait(false);

        var changeCommand = new ChatsBackend_Change(chatId, expectedVersion, change.RequireValid());
        if (change.IsCreate(out var chatDiff1)) {
            var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
            account.Require(AccountFull.MustBeActive);
            changeCommand = changeCommand with {
                OwnerId = account.Id,
            };
            var placeId = chatDiff1.PlaceId;
            await ValidatePlaceChatChangeConstraints(placeId, chatDiff1).ConfigureAwait(false);
        }
        else {
            chatId.Require();
            if (chatId.IsThread(out var threadChatId)) {
                if (change.IsUpdate(out var chatDiff2)) {
                    chat.Require().Rules.Permissions.Require(ChatPermissions.EditProperties);
                    ValidateThreadChatChangeConstraints(chatDiff2);
                }
                else if (change.Kind is ChangeKind.Remove) {
                    var parentChat = await Get(session, threadChatId.ParentChatId, cancellationToken).ConfigureAwait(false);
                    parentChat.Require().Rules.Permissions.Require(ChatPermissions.Owner); // Thread can be removed only by parent chat owner.
                }
                else
                    throw StandardError.Internal("Invalid ChangeKind");
            }
            else {
                var requiredPermissions = change.Remove
                    ? ChatPermissions.Owner
                    : ChatPermissions.EditProperties;
                chat.Require().Rules.Permissions.Require(requiredPermissions);

                if (change.IsUpdate(out var chatDiff2)) {
                    await ValidatePlaceChatChangeConstraints((chat.Id as PlaceChatId)?.PlaceId, chatDiff2)
                        .ConfigureAwait(false);
                    if (chat.Id is PeerChatId)
                        ValidatePeerChatChangeConstraints(chatDiff2);
                }
            }
        }

        chat = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        if (change.Create.HasValue)
            await Authors.EnsureJoined(session, chat.Id, cancellationToken).ConfigureAwait(false);
        return chat;

        void ValidateThreadChatChangeConstraints(ChatDiff chatDiff)
        {
            var isReadOnlyProperty = chatDiff.IsPublic.HasValue
                || chatDiff.PlaceId is not null
                || chatDiff.IsTemplate.HasValue
                || chatDiff.TemplateId.HasValue
                || chatDiff.TemplatedForUserId.HasValue
                || chatDiff.Kind.HasValue
                || chatDiff.IsArchived.HasValue
                || chatDiff.MediaId is not null
                || chatDiff.SystemTag.HasValue
                || chatDiff.AliasId is not null
                || chatDiff.AllowAnonymousAuthors.HasValue
                || chatDiff.AllowGuestAuthors.HasValue;
            if (isReadOnlyProperty)
                throw StandardError.Constraint("It's allowed to change only Title or Description for the thread.");
        }

        async Task ValidatePlaceChatChangeConstraints(PlaceId? placeId, ChatDiff chatDiff)
        {
            if (placeId is null)
                return;

            var place = await Places.Get(session, placeId, cancellationToken).ConfigureAwait(false);
            if (place == null)
                throw StandardError.Constraint("Requested place is unavailable.");

            var placeMember = place.Rules.Author;
            if (placeMember == null)
                throw StandardError.Constraint("Only place members can add chats.");
            var isOwner = place.Rules.IsOwner();
            if (!isOwner && chatDiff.IsPublic == true)
                throw StandardError.NotEnoughPermissions("Make chat public");
        }

        static void ValidatePeerChatChangeConstraints(ChatDiff chatDiff)
        {
            chatDiff.Title.RequireNull("Title");
            chatDiff.Description.RequireNull("Description");
            chatDiff.MediaId.RequireNull("MediaId");
            chatDiff.AliasId.RequireNull("AliasId");
            chatDiff.IsArchived.HasValue.RequireFalse("IsArchived");
            chatDiff.IsPublic.HasValue.RequireFalse("IsPublic");
            chatDiff.IsTemplate.HasValue.RequireFalse("IsTemplate");
            chatDiff.TemplateId.HasValue.RequireFalse("TemplateId");
            chatDiff.TemplatedForUserId.HasValue.RequireFalse("TemplatedForUserId");
            chatDiff.Kind.HasValue.RequireFalse("Kind");
            chatDiff.SystemTag.HasValue.RequireFalse("SystemTag");
            chatDiff.AllowAnonymousAuthors.HasValue.RequireFalse("AllowAnonymousAuthors");
            chatDiff.AllowGuestAuthors.HasValue.RequireFalse("AllowGuestAuthors");
            chatDiff.PlaceId.RequireNull("PlaceId");
        }
    }

    // [CommandHandler]
    public virtual async Task<ChatEntry> OnUpsertEntry(Chats_UpsertEntry command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (session, chatId, localId, text, repliedEntryLid) =
            (command.Session, command.ChatId, command.LocalId, command.Text, command.RepliedEntryLid);
        ThrowIfPlaceRootChat(chatId);

        var author = await Authors.EnsureJoined(session, chatId, cancellationToken).ConfigureAwait(false);
        var chat = await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(ChatPermissions.Write);
        var attachments = command.Attachments;
        if (attachments.Length > 0 || command.HasUploadingAttachments)
            chat.Rules.Permissions.Require(ChatPermissions.Upload);
        if (string.IsNullOrWhiteSpace(text) && attachments.Length == 0 && !command.HasUploadingAttachments)
            throw StandardError.Constraint("Sorry, you can't post empty messages.");

        ChatEntry textEntry;
        if (localId is { } vLocalId) {
            // Update
            var chatEntryId = ChatEntryId.New(chatId, vLocalId);
            textEntry = await this
                .GetEntry(session, chatEntryId, cancellationToken)
                .Require(ChatEntry.MustNotBeRemoved)
                .ConfigureAwait(false);

            // Check constraints
            if (textEntry.AuthorId != author.Id)
                throw StandardError.Unauthorized("You can edit only your own messages.");
            if (textEntry.IsContentStreaming)
                throw StandardError.Constraint("Streaming messages cannot be edited.");
            if (textEntry.Forwarded is not null && command.Text != textEntry.Content)
                throw StandardError.Constraint("Forwarded messages cannot be edited.");
            if (repliedEntryLid.IsSome(out var v) && textEntry.RepliedEntryLid != v)
                throw StandardError.Constraint("Replied entry Id cannot be changed.");

            var diff = new ChatEntryDiff {
                Content = text,
                RepliedEntryLid = repliedEntryLid,
                Attachments = command.Attachments,
            };

            if (textEntry.HasAudio) {
                // If the new text contains markup (mentions, URLs, bold, etc.),
                // it must become a text message - audio playback can't render markup properly
                var chatMarkupHub = ChatMarkupHubFactory[textEntry.ChatId];
                var parsedMarkup = chatMarkupHub.Parser.Parse(text);
                if (!parsedMarkup.IsPlainText()) {
                    // Has markup: strip audio link, convert to text message
                    diff = diff with { Audio = null };
                }
                else if (textEntry.Audio is { TimeMap.IsDegenerate: false } audio) {
                    // Audio-aware edit: remap TimeMap or strip audio link
                    var remapResult = LinearMapDtwRemapper.RemapWithSimilarity(
                        textEntry.Content, text, audio.TimeMap,
                        LinearMapAlignmentMode.UserEditedTranscript);
                    if (remapResult is { Similarity: >= LinearMapRemapResult.MinorEditSimilarityThreshold, Map.IsDegenerate: false }) {
                        // Minor edit: keep audio, update TimeMap
                        diff = diff with { Audio = textEntry.Audio! with { TimeMap = remapResult.Map } };
                    }
                    else {
                        // Major edit: strip audio link
                        diff = diff with { Audio = null };
                    }
                }
                else {
                    // Degenerate TimeMap: can't remap, strip audio
                    diff = diff with { Audio = null };
                }
            }

            var upsertCommand = new ChatsBackend_ChangeEntry(
                chatEntryId,
                null,
                Change.Update(diff));
            textEntry = await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        }
        else { // Create
            // In peer chats, when the caller isn't in the recipient's contacts and
            // the recipient hasn't replied, ChatPermissions.WriteAudio (and other
            // content-type flags) are stripped. Use that as the cheap signal to
            // enforce the creation cap. Edits remain unaffected since this branch
            // only runs on create.
            if (chatId is PeerChatId && !chat.Rules.Has(ChatPermissions.WriteAudio)) {
                var hasReachedLimit = await HasReachedNonContactPeerLimit(chatId, author.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (hasReachedLimit)
                    throw StandardError.Constraint(
                        $"You can send up to {Constants.Chat.NonContactPeerMessageLimit} messages until this user adds you to their contacts or replies.");
            }

            var commandResult = await TryHandleAdminCommand(session, chatId, author, text, cancellationToken)
                .ConfigureAwait(false);
            if (commandResult != null)
                return commandResult;

            var chatEntryId = ChatEntryId.New(chatId, 0);
            var upsertCommand = new ChatsBackend_ChangeEntry(
                chatEntryId,
                null,
                Change.Create(new ChatEntryDiff {
                    AuthorId = author.Id,
                    Content = text,
                    RepliedEntryLid = repliedEntryLid,
                    Forwarded = command.Forwarded,
                    Attachments = attachments.Length == 0 ? null : attachments,
                    ClientId = command.ClientId,
                }));
            textEntry = await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
            if (chatId is PeerChatId peerChatId)
                await EnsureOwnPeerContactStored(peerChatId, chat.Rules.Account.Require().Id, cancellationToken)
                    .ConfigureAwait(false);
        }

        return textEntry;
    }

    // [CommandHandler]
    public virtual async Task OnRemoveEntry(Chats_RemoveEntry command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, localId) = command;
        ThrowIfPlaceRootChat(chatId);

        var author = await Authors.EnsureJoined(session, chatId, cancellationToken).ConfigureAwait(false);
        var chat = await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(ChatPermissions.Write);

        var chatEntryId = ChatEntryId.New(chatId, localId);
        await RemoveTextEntry(session, chat, chatEntryId, author, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRestoreEntry(Chats_RestoreEntry command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, localId) = command;
        var author = await Authors.EnsureJoined(session, chatId, cancellationToken).ConfigureAwait(false);
        var chat = await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(ChatPermissions.Write);

        var chatEntryId = ChatEntryId.New(chatId, localId);
        await RestoreTextEntry(session,
                chat,
                chatEntryId,
                author,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRemoveEntries(Chats_RemoveEntries command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, localIds) = command;
        ThrowIfPlaceRootChat(chatId);

        var author = await Authors.EnsureJoined(session, chatId, cancellationToken).ConfigureAwait(false);
        var chat = await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(ChatPermissions.Write);

        foreach (var localId in localIds) {
            var chatEntryId = ChatEntryId.New(chatId, localId);
            await RemoveTextEntry(session, chat, chatEntryId, author, cancellationToken).ConfigureAwait(false);
        }
    }

    // [CommandHandler]
    public virtual async Task OnRestoreEntries(Chats_RestoreEntries command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, localIds) = command;
        var author = await Authors.EnsureJoined(session, chatId, cancellationToken).ConfigureAwait(false);
        var chat = await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(ChatPermissions.Write);

        foreach (var localId in localIds) {
            var chatEntryId = ChatEntryId.New(chatId, localId);
            await RestoreTextEntry(session, chat, chatEntryId, author, cancellationToken).ConfigureAwait(false);
        }
    }

    // [CommandHandler]
    public virtual async Task<Chat> OnGetOrCreateFromTemplate(Chats_GetOrCreateFromTemplate command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (session, templateChatId) = command;
        var templateChat = await Get(session, templateChatId, cancellationToken).ConfigureAwait(false);
        templateChat.Require(Chat.MustBeTemplate);

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var chat = await Backend.GetTemplatedChatFor(templateChatId, account.Id, cancellationToken).ConfigureAwait(false);
        if (chat != null)
            return chat;

        var templateAuthorIds = await AuthorsBackend.ListAuthorIds(templateChatId, cancellationToken).ConfigureAwait(false);
        var templateAuthors = await templateAuthorIds
            .Select(aId => AuthorsBackend.Get(templateChatId, aId, RequestedAuthorKind.Full, cancellationToken))
            .Collect(4, cancellationToken) // NOTE(AY): Why 4? AK, please add comment
            .ConfigureAwait(false);
        var authorRoles = await templateAuthorIds
            .Select(async aId => (
                AuthorId: aId,
                Roles: await RolesBackend.List(templateChatId,
                    aId,
                    false,
                    false,
                    cancellationToken).ConfigureAwait(false))
            )
            .Collect(4, cancellationToken) // NOTE(AY): Why 4? AK, please add comment
            .ConfigureAwait(false);
        var templateOwner = authorRoles.FirstOrDefault(x => x.Roles.Any(r => r.SystemRole == SystemRole.Owner));

        // clone template chat
        var cloneCommand = new ChatsBackend_Change(
            null, null,
            new Change<ChatDiff> {
                Create = new ChatDiff {
                    Title = templateChat.Title,
                    MediaId = templateChat.MediaId,
                    Kind = ChatKind.Group,
                    IsPublic = true,
                    IsTemplate = false,
                    TemplateId = templateChatId,
                    TemplatedForUserId = account.Id,
                    AllowAnonymousAuthors = false,
                    AllowGuestAuthors = true,
                },
            },
            templateAuthors.Single(a => a?.Id == templateOwner.AuthorId)?.UserId // Owner is mandatory
        );

        var cloned = await Commander.Call(cloneCommand, true, cancellationToken).ConfigureAwait(false);
        var chatId = cloned.Id;

        // copy existing template authors and their roles
        var clonedAuthors = new List<AuthorFull>();
        foreach (var templateAuthor in templateAuthors.Where(ta => ta != null)) {
            var cloneAuthorCommand = new AuthorsBackend_Upsert(
                chatId,
                null,
                templateAuthor!.UserId,
                ExpectedVersion: null,
                new AuthorDiff {
                    AvatarId = templateAuthor.AvatarId,
                },
                DoNotNotify: true);
            var clonedAuthor = await Commander.Call(cloneAuthorCommand, true, cancellationToken).ConfigureAwait(false);
            clonedAuthors.Add(clonedAuthor);
        }
        var authorMap = templateAuthors
            .Where(a => a != null)
            .Join(clonedAuthors,
                a => a!.UserId,
                a => a.UserId,
                (l, r) => (TemplateAuthorId: l!.Id, CloneAuthorId: r.Id))
            .ToDictionary(x => x.TemplateAuthorId, x => x.CloneAuthorId);
        var roleAuthors = authorRoles
            .SelectMany(x => x.Roles, (x, r) => (x.AuthorId, Role: r))
            .Where(x => x.Role.SystemRole is not SystemRole.Anyone and not SystemRole.None and not SystemRole.Owner) // Owner is already registered
            .GroupBy(x => x.Role.Id,
                (_, xs) => {
                    var tuples = xs.ToList();
                    return (tuples.FirstOrDefault().Role, AuthorIds: tuples.Select(x => authorMap[x.AuthorId]).ToArray());
                })
            .ToList();

        foreach (var (role, roleAuthorIds) in roleAuthors) {
            var createOwnersRoleCmd = new RolesBackend_Change(cloned.Id, null, null, new() {
                Create = new RoleDiff {
                    Picture = role.Picture,
                    Name = role.Name,
                    SystemRole = role.SystemRole,
                    Permissions = role.Permissions,
                    AuthorIds = new SetDiff<AuthorId[], AuthorId> {
                        AddedItems = roleAuthorIds,
                    },
                },
            });
            await Commander.Call(createOwnersRoleCmd, true, cancellationToken).ConfigureAwait(false);
        }

        // join guest author
        var avatarIds = await Avatars.ListOwnAvatarIds(session, cancellationToken).ConfigureAwait(false);
        var avatars = await avatarIds
            .Select(aId => Avatars.GetOwn(session, aId, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        var guestAvatar = avatars
            .Where(a => a != null)
            .FirstOrDefault(a => a!.Name == Avatar.GuestName);
        if (guestAvatar == null) {
            var diff = AvatarDiff.FromFull(
                new AvatarFull(account.Id) { Name = "Guest" }
                    .WithMissingPropertiesFrom(account.Avatar));
            var createAvatarCommand = new Avatars_Change(session, Symbol.Empty, null, Change.Create(diff));
            var newAvatar = await Commander.Call(createAvatarCommand, true, cancellationToken).ConfigureAwait(false);
            guestAvatar = newAvatar;
        }
        var createAuthorCommand = new AuthorsBackend_Upsert(
            chatId,
            null,
            account.Id,
            ExpectedVersion: null,
            new AuthorDiff {
                AvatarId = guestAvatar.Id,
            });
        await Commander.Run(createAuthorCommand, cancellationToken).ConfigureAwait(false);

        return cloned;
    }

    // [CommandHandler]
    public virtual async Task<Unit> OnForwardEntries(Chats_ForwardEntries command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default; // It just spawns other commands, so nothing to do here

        var (session, chatId, chatEntryIds, destinationChatIds) = command;
        await Authors.EnsureJoined(session, chatId, cancellationToken).ConfigureAwait(false);
        var chat = await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(ChatPermissions.Read);

        var chatEntries = await chatEntryIds
            .OrderBy(x => x.LocalId)
            .Select(chatEntryId => this.GetEntry(session, chatEntryId, cancellationToken)
                .Require(ChatEntry.MustNotBeRemoved)
                .AsTask())
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        foreach (var destinationChatId in destinationChatIds) {
            var destinationChat = await Get(session, destinationChatId, cancellationToken).Require().ConfigureAwait(false);
            await Authors.EnsureJoined(session, destinationChatId, cancellationToken).ConfigureAwait(false);
            destinationChat.Rules.Permissions.Require(ChatPermissions.Write);

            foreach (var chatEntry in chatEntries) {
                var forwarded = chatEntry.Forwarded;
                var forwardedChatTitle = forwarded?.ChatTitle.NullIfEmpty() ?? chat.Title;
                var forwardedChatEntryId = forwarded is not null
                    ? forwarded.ChatEntryId is not null && forwarded.ChatEntryId.ChatId.Kind != ChatKind.Peer
                        ? forwarded.ChatEntryId
                        : null
                    : chatEntry.ChatId.Kind == ChatKind.Peer
                        ? null
                        : chatEntry.Id;
                var forwardedBeginsAt = forwarded?.BeginsAt ?? chatEntry.BeginsAt;
                var forwardedAuthorId = forwarded?.AuthorId ?? chatEntry.AuthorId;
                var forwardedAuthorName = forwarded?.AuthorName ?? "";
                if (forwardedAuthorName.IsNullOrEmpty()) {
                    var forwardedAuthor = await AuthorsBackend
                        .Get(forwardedAuthorId.ChatId, forwardedAuthorId, RequestedAuthorKind.Full, cancellationToken)
                        .ConfigureAwait(false);
                    forwardedAuthorName = forwardedAuthor!.Avatar.Name;
                }

                var cmd = new Chats_UpsertEntry(session, destinationChatId, null) {
                    Text = chatEntry.Content,
                    Forwarded = new ChatEntryForwarded {
                        ChatEntryId = forwardedChatEntryId,
                        AuthorId = forwardedAuthorId,
                        BeginsAt = forwardedBeginsAt,
                        ChatTitle = forwardedChatTitle,
                        AuthorName = forwardedAuthorName,
                    },
                    Attachments = chatEntry.Attachments.Select(x => new ChatEntryAttachment {
                        MediaId = x.MediaId,
                        ThumbnailMediaId = x.ThumbnailMediaId,
                    }).ToArray(),
                };
                // NOTE: may stick due to infinite connect timeout for the command
                await Commander.Run(cmd, CancellationToken.None).ConfigureAwait(false);
            }
        }

        return default;
    }

    // [CommandHandler]
    public virtual async Task<Unit> OnForwardAttachment(Chats_ForwardAttachment command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default;

        var (session, chatEntryId, attachmentIndex, destinationChatIds) = command;
        var sourceChatId = chatEntryId.ChatId;
        await Authors.EnsureJoined(session, sourceChatId, cancellationToken).ConfigureAwait(false);
        var sourceChat = await Get(session, sourceChatId, cancellationToken).Require().ConfigureAwait(false);
        sourceChat.Rules.Permissions.Require(ChatPermissions.Read);

        var chatEntry = await this.GetEntry(session, chatEntryId, cancellationToken)
            .Require(ChatEntry.MustNotBeRemoved)
            .ConfigureAwait(false);
        var attachment = chatEntry.Attachments.FirstOrDefault(a => a.Index == attachmentIndex)
            ?? throw StandardError.NotFound<ChatEntryAttachment>("Attachment not found in the source entry.");

        foreach (var destinationChatId in destinationChatIds) {
            var destinationChat = await Get(session, destinationChatId, cancellationToken).Require().ConfigureAwait(false);
            await Authors.EnsureJoined(session, destinationChatId, cancellationToken).ConfigureAwait(false);
            destinationChat.Rules.Permissions.Require(ChatPermissions.Write);

            var cmd = new Chats_UpsertEntry(session, destinationChatId, null) {
                Text = "",
                Attachments = [
                    new ChatEntryAttachment {
                        MediaId = attachment.MediaId,
                        ThumbnailMediaId = attachment.ThumbnailMediaId,
                    },
                ],
            };
            await Commander.Run(cmd, CancellationToken.None).ConfigureAwait(false);
        }

        return default;
    }

    // [CommandHandler]
    public virtual async Task<Chat_CopyChatResult> OnCopyChat(Chat_CopyChat command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (session, sourceChatId, placeId, correlationId) = command;
        var hasChanges = false;
        var hasErrors = false;
        Log.LogInformation("-> OnCopyChat({CorrelationId}): copy chat '{ChatId}' to place '{PlaceId}'",
            correlationId, sourceChatId.Value, placeId);
        var chat = await Get(session, sourceChatId, cancellationToken).Require().ConfigureAwait(false);
        Log.LogInformation("Chat for chat id '{ChatId}' is {Chat} ({CorrelationId})",
            sourceChatId, chat, correlationId);
        if (chat.Id.Kind != ChatKind.Group && chat.Id.Kind != ChatKind.Place)
            throw StandardError.Constraint("Only group or place chats can be copied to a Place.");
        if (chat.Id is PlaceChatId placeChatId1 && placeChatId1.PlaceId == placeId)
            throw StandardError.Constraint("Can't copy place chat to the same Place.");
        if (!chat.Rules.IsOwner())
            throw StandardError.Constraint("You must be the Owner of this chat to perform the migration.");
        var place = await Places.Get(session, placeId, cancellationToken).Require().ConfigureAwait(false);
        if (!place.Rules.IsOwner())
            throw StandardError.Constraint("You should be place owner to perform 'copy chat to place' operation.");

        var localChatId = sourceChatId is PlaceChatId sourcePlaceChatId
            ? sourcePlaceChatId.LocalChatId
            : sourceChatId.Value;
        var newChatId = PlaceChatId.Parse(PlaceChatId.Format(placeId, localChatId));

        {
            var backendCmd = new ChatBackend_CopyChat(sourceChatId, placeId, correlationId);
            var result = await Commander.Call(backendCmd, true, cancellationToken).ConfigureAwait(false);
            hasChanges |= result.HasChanges;
            hasErrors |= result.HasErrors;
        }
        {
            // Ensure chat is listed in place chat list for the user who is performing chat copying.
            var author = await Authors.GetOwn(session, newChatId, cancellationToken).ConfigureAwait(false);
            if (author != null) {
                var userId = author.UserId;
                var contactId = ContactId.NewAny(userId, newChatId);
                var contact = await ContactsBackend.Get(userId, contactId, cancellationToken).ConfigureAwait(false);
                if (!contact.IsRegular) {
                    var backendChangeCmd = new ContactsBackend_Change(
                        contactId,
                        contact.HasVersion() ? contact.Version : null,
                        Change.Upsert(contact with { State = ContactState.Regular }));
                    await Commander.Call(backendChangeCmd, true, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        {
            var textEntryRange = await Backend.GetLidRange(newChatId, false, cancellationToken).ConfigureAwait(false);
            var maxEntryId = textEntryRange.End > 0 ? textEntryRange.End - 1 : 0;
            var userIds = await AuthorsBackend.ListUserIds(newChatId, cancellationToken).ConfigureAwait(false);
            var updateUserChatSettingCount = 0;
            var updateChatPositionCount = 0;
            foreach (var userId in userIds) {
                var updateChatUserSettingsTask = UpdateChatUserSettings(userId);
                var updateChatPositionsTask = UpdateChatPosition(userId, maxEntryId);
                await Task.WhenAll(updateChatUserSettingsTask, updateChatUserSettingsTask).ConfigureAwait(false);
                if (await updateChatUserSettingsTask.ConfigureAwait(false))
                    updateUserChatSettingCount++;
                if (await updateChatPositionsTask.ConfigureAwait(false))
                    updateChatPositionCount++;
            }

            Log.LogInformation("OnCopyChat({CorrelationId}): Updated {Count} ChatUserSettings records",
                correlationId, updateUserChatSettingCount);
            Log.LogInformation("OnCopyChat({CorrelationId}): Updated {Count} ChatPositions records",
                correlationId, updateChatPositionCount);

            hasChanges |= updateUserChatSettingCount > 0;
            hasChanges |= updateChatPositionCount > 0;
        }

        Log.LogInformation("<- OnCopyChat({CorrelationId})", correlationId);
        return new Chat_CopyChatResult(hasChanges, hasErrors);

        async Task<bool> UpdateChatUserSettings(UserId userId)
        {
            var userKvas = ServerKvasBackend.ForUser(userId);
            var chatUserSettings = await userKvas.ChatUserSettings(sourceChatId).Get(cancellationToken).ConfigureAwait(false);
            if (chatUserSettings == ChatUserSettings.Default)
                return false;

            await userKvas.ChatUserSettings(newChatId).Set(chatUserSettings, cancellationToken).ConfigureAwait(false);
            return true;
        }

        async Task<bool> UpdateChatPosition(UserId userId, long maxEntryId)
        {
            if (maxEntryId <= 0)
                return false;

            var oldChatPosition = await ChatPositionsBackend
                .Get(userId, sourceChatId, ChatPositionKind.Read, cancellationToken)
                .ConfigureAwait(false);
            if (oldChatPosition.EntryLid <= 0)
                return false;

            var newChatPosition = await ChatPositionsBackend
                .Get(userId, newChatId, ChatPositionKind.Read, cancellationToken)
                .ConfigureAwait(false);
            var newPosition = Math.Min(oldChatPosition.EntryLid, maxEntryId);
            if (newPosition <= newChatPosition.EntryLid)
                return false;

            var chatPosition = new ChatPosition(newPosition, oldChatPosition.Origin);
            var backendCommand = new ChatPositionsBackend_Set(userId, newChatId, ChatPositionKind.Read, chatPosition);
            await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    // [CommandHandler]
    public virtual async Task OnPublishCopiedChat(Chat_PublishCopiedChat command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, newChatId, sourceChatId) = command;

        var localChatId = sourceChatId is PlaceChatId sourcePlaceChatId
            ? ChatId.Parse(sourcePlaceChatId.LocalChatId)
            : sourceChatId;
        if (newChatId.LocalChatId != localChatId.Value)
            throw StandardError.Constraint("New chat is seems not a copy of provided source chat.");

        var newChat = await Get(session, newChatId, cancellationToken).Require().ConfigureAwait(false);
        if (!newChat.Rules.IsOwner())
            throw StandardError.Constraint("You must be the Owner of this chat to perform this operation.");

        var place = await Places.Get(session, newChatId.PlaceId, cancellationToken).Require().ConfigureAwait(false);
        if (!place.Rules.IsOwner())
            throw StandardError.Constraint("You must be a chat's place owner to perform this operation.");

        var sourceChat = await Get(session, sourceChatId, cancellationToken).ConfigureAwait(false);
        var chatCopyState = await GetChatCopyState(session, newChatId, cancellationToken).ConfigureAwait(false);

        if (sourceChat != null && newChat.IsPublic != sourceChat.IsPublic) {
            var changeChatCmd = new Chats_Change(session,
                newChat.Id,
                newChat.Version,
                Change.Update(new ChatDiff {
                    IsPublic = sourceChat.IsPublic,
                }));
            await Commander.Call(changeChatCmd, true, cancellationToken).ConfigureAwait(false);
        }

        var publishContactsCmd = new ContactsBackend_PublishCopiedChat(newChatId);
        await Commander.Call(publishContactsCmd, true, cancellationToken).ConfigureAwait(false);

        if (sourceChat is { IsArchived: false }) {
            var archiveChatCmd = new Chats_Change(session,
                sourceChat.Id,
                null,
                Change.Update(new ChatDiff {
                    IsArchived = true,
                }));
            await Commander.Call(archiveChatCmd, true, cancellationToken).ConfigureAwait(false);
        }

        if (chatCopyState != null && !chatCopyState.IsPublished) {
            var publishCopiedChatCmd = new ChatsBackend_ChangeChatCopyState(chatCopyState.Id,
                chatCopyState.Version,
                Change.Update(new ChatCopyStateDiff {
                    IsPublished = true,
                }));
            await Commander.Call(publishCopiedChatCmd, true, cancellationToken).ConfigureAwait(false);
        }
    }

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<ReadPositionsStat> GetReadPositionsStatInternal(ChatId chatId, CancellationToken cancellationToken)
    {
        var statBackend = await Backend.GetReadPositionsStat(chatId, cancellationToken).ConfigureAwait(false);
        if (statBackend == null)
            return new ReadPositionsStat(chatId, long.MaxValue, []);

        var positions = statBackend.TopReadPositions;
        var top2AuthorReadPositions = (await positions
                .Select(async c => {
                    var authorId = (AuthorId?)null;
                    using (var _ = Computed.BeginIsolation()) {
                        // Do not capture dependency, we just need an author id
                        var author = await AuthorsBackend.GetByUserId(chatId,
                                c.UserId!,
                                RequestedAuthorKind.Full,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (author != null)
                            authorId = author.Id;
                    }
                    return (AuthorId: authorId, c.EntryLid);
                })
                .Collect(cancellationToken)
                .ConfigureAwait(false))
            .Where(c => c.AuthorId != null)
            .Select(c => new AuthorReadPosition(c.AuthorId!, c.EntryLid))
            .ToArray();

        return new ReadPositionsStat(chatId, statBackend.StartTrackingEntryLid, top2AuthorReadPositions);
    }

    // Private methods

    private static void ThrowIfPlaceRootChat(ChatId chatId)
    {
        if (chatId is PlaceChatId { IsRoot: true })
            throw StandardError.Constraint(
                "A place's root chat can't hold messages — post to one of the place's chats instead.");
    }

    private async ValueTask RequireCanRead(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var rules = await GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanRead())
            throw StandardError.NotFound<Chat>();
    }

    private async ValueTask<bool> CanRead(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var rules = await GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        return rules.CanRead();
    }

    private async Task<bool> HasReachedNonContactPeerLimit(
        ChatId chatId,
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var messageLimit = Constants.Chat.NonContactPeerMessageLimit;
        var firstAuthors = await Backend.GetFirstEntryAuthors(chatId, messageLimit, true, cancellationToken).ConfigureAwait(false);
        return firstAuthors.Count == 1 && firstAuthors.Contains(authorId);
    }

    private async Task EnsureOwnPeerContactStored(
        PeerChatId chatId,
        UserId ownerId,
        CancellationToken cancellationToken)
    {
        var contactId = ContactId.NewUser(ownerId, chatId.AnotherUserId(ownerId));
        var contact = await ContactsBackend.Get(ownerId, contactId, cancellationToken).ConfigureAwait(false);
        if (contact.IsRegular)
            return;

        var command = new ContactsBackend_Change(
            contactId,
            contact.HasVersion() ? contact.Version : null,
            Change.Upsert(contact with { State = ContactState.Regular }));
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PrincipalId> GetOwnPrincipalId(
        Session session, ChatId chatId,
        CancellationToken cancellationToken)
    {
        // NOTE(DF): PrincipalId seems to be legacy stuff. Now we have userId even for guest users.
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author != null)
            return author.Id;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return account.Id;
    }

    private async Task RemoveTextEntry(
        Session session,
        Chat chat,
        ChatEntryId chatEntryId,
        Author author,
        CancellationToken cancellationToken)
    {
        var textEntry = await this
            .GetEntry(session, chatEntryId, cancellationToken)
            .Require(ChatEntry.MustNotBeRemoved)
            .ConfigureAwait(false);

        // Check constraints
        if (!(textEntry.AuthorId == author.Id || chat.Rules.IsOwner() || chat.Id.Kind == ChatKind.Peer))
            throw StandardError.Unauthorized("You're not allowed to remove this message.");

        await Remove(chatEntryId).ConfigureAwait(false);
        return;

        async Task Remove(ChatEntryId entryId1) {
            var removeCommand = new ChatsBackend_ChangeEntry(entryId1, null, Change.Remove<ChatEntryDiff>());
            await Commander.Call(removeCommand, true, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RestoreTextEntry(
        Session session,
        Chat chat,
        ChatEntryId chatEntryId,
        Author author,
        CancellationToken cancellationToken)
    {
        var textEntry = await GetRemovedEntry(chatEntryId).ConfigureAwait(false);

        // Check constraints
        if (textEntry == null)
            return;

        if (!(textEntry.AuthorId == author.Id || chat.Rules.IsOwner() || chat.Id.Kind == ChatKind.Peer))
            throw StandardError.Unauthorized("You're not allowed to restore this message.");

        await Restore(chatEntryId).ConfigureAwait(false);
        return;

        async Task Restore(ChatEntryId entryId1) {
            var restoreCommand = new ChatsBackend_ChangeEntry(
                entryId1,
                null,
                Change.Update(new ChatEntryDiff {
                    IsRemoved = false,
                }));
            await Commander.Call(restoreCommand, true, cancellationToken).ConfigureAwait(false);
        }

        async ValueTask<ChatEntry?> GetRemovedEntry(ChatEntryId entryId) {
            await Get(session, chat.Id, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the chat
            return await Backend.GetRemovedEntry(entryId, cancellationToken).ConfigureAwait(false);
        }
    }
}
