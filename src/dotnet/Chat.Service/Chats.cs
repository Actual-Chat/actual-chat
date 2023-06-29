using ActualChat.Chat.Db;
using ActualChat.Contacts;
using ActualChat.Invite;
using ActualChat.Invite.Backend;
using ActualChat.Kvas;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class Chats : DbServiceBase<ChatDbContext>, IChats
{
    private IAccounts Accounts { get; }
    private IAuthors Authors { get; }
    private IAuthorsBackend AuthorsBackend { get; }
    private IAvatars Avatars { get; }
    private IContactsBackend ContactsBackend { get; }
    private IInvitesBackend InvitesBackend { get; }
    private IServerKvas ServerKvas { get; }
    private IChatsBackend Backend { get; }
    private IRolesBackend RolesBackend { get; }

    public Chats(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Authors = services.GetRequiredService<IAuthors>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        Avatars = services.GetRequiredService<IAvatars>();
        ContactsBackend = services.GetRequiredService<IContactsBackend>();
        InvitesBackend = services.GetRequiredService<IInvitesBackend>();
        ServerKvas = services.ServerKvas();
        Backend = services.GetRequiredService<IChatsBackend>();
        RolesBackend = services.GetRequiredService<IRolesBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Backend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chatId.Kind == ChatKind.Peer) {
            var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
            var contactId = new ContactId(account.Id, chatId, ParseOrNone.Option);
            if (contactId.IsNone)
                return null;

            var contact = await ContactsBackend.Get(account.Id, contactId, cancellationToken).ConfigureAwait(false);
            if (contact.Account == null)
                return null; // No peer account

            chat ??= new Chat(chatId);
            chat = chat with {
                Title = contact.Account.Avatar.Name,
                Picture = contact.Account.Avatar.Media,
            };
        }
        else if (chat == null)
            return null;

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
        ChatEntryKind entryKind,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
    {
        await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the chat
        return await Backend.GetTile(chatId, entryKind, idTileRange, false, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long>? idTileRange,
        CancellationToken cancellationToken)
    {
        await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the chat
        return await Backend.GetEntryCount(chatId, entryKind, idTileRange, false, cancellationToken).ConfigureAwait(false);
    }

    // Note that it returns (firstId, lastId + 1) range!
    // [ComputeMethod]
    public virtual async Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        CancellationToken cancellationToken)
    {
        await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the chat
        return await Backend.GetIdRange(chatId, entryKind, false, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<AuthorRules> GetRules(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var principalId = await GetOwnPrincipalId(session, chatId, cancellationToken).ConfigureAwait(false);
        var rules = await Backend.GetRules(chatId, principalId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanRead() && chatId.Kind != ChatKind.Peer) {
            // Has invite = same as having read permission
            var activationKeyOpt = await ServerKvas
                .GetClient(session)
                .TryGet<string>(ServerKvasInviteKey.ForChat(chatId), cancellationToken)
                .ConfigureAwait(false);
            if (activationKeyOpt.IsSome(out var activationKey)) {
                var isValid = await InvitesBackend.IsValid(activationKey, cancellationToken).ConfigureAwait(false);
                if (isValid)
                    rules = rules with {
                        Permissions = (rules.Permissions | ChatPermissions.Join).AddImplied(),
                    };
            }
        }
        return rules;
    }

    // [ComputeMethod]
    public virtual async Task<ChatNews> GetNews(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await Get(session, chatId, cancellationToken).ConfigureAwait(false); // Make sure we can read the chat
        if (chat == null)
            return default;

        return await Backend.GetNews(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<Author>> ListMentionableAuthors(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the chat
        var authorIds = await AuthorsBackend.ListAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var authors = await authorIds
            .Select(id => Authors.Get(session, chatId, id, cancellationToken))
            .Collect() // Add concurrency
            .ConfigureAwait(false);
        return authors
            .SkipNullItems()
            .OrderBy(a => a.Avatar.Name, StringComparer.Ordinal)
            .ToApiArray();
    }

    // Not a [ComputeMethod]!
    public virtual async Task<ChatEntry?> FindNext(
        Session session,
        ChatId chatId,
        long? startEntryId,
        string text,
        CancellationToken cancellationToken)
    {
        text.RequireMaxLength(Constants.Chat.MaxSearchFilterLength, "text.length");

        var chat = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbEntry = await dbContext.ChatEntries.OrderByDescending(x => x.LocalId)
            .FirstOrDefaultAsync(x => (startEntryId == null || x.LocalId < startEntryId) && x.Content.Contains(text), cancellationToken)
            .ConfigureAwait(false);
        return dbEntry?.ToModel();
    }

    // [CommandHandler]
    public virtual async Task<Chat> OnChange(Chats_Change command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId, expectedVersion, change) = command;
        var chat = chatId.IsNone ? null
            : await Get(session, chatId, cancellationToken).ConfigureAwait(false);

        var changeCommand = new ChatsBackend_Change(chatId, expectedVersion, change.RequireValid());
        if (change.Create.HasValue) {
            var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
            account.Require(AccountFull.MustBeActive);
            changeCommand = changeCommand with {
                OwnerId = account.Id,
            };
        }
        else {
            var requiredPermissions = change.Remove
                ? ChatPermissions.Owner
                : ChatPermissions.EditProperties;
            chat.Require().Rules.Permissions.Require(requiredPermissions);
        }

        chat = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        if (change.Create.HasValue)
            await Authors.EnsureJoined(session, chat.Id, cancellationToken).ConfigureAwait(false);
        return chat;
    }

    public virtual async Task<ChatEntry> OnUpsertTextEntry(Chats_UpsertTextEntry command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId, localId, text, repliedChatEntryId) = command;
        var author = await Authors.EnsureJoined(session, chatId, cancellationToken).ConfigureAwait(false);
        var chat = await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(ChatPermissions.Write);
        if (string.IsNullOrWhiteSpace(text) && command.Attachments.IsEmpty)
            throw StandardError.Constraint("Sorry, you can't post empty messages.");

        ChatEntry textEntry;
        if (localId is { } vLocalId) {
            // Update
            var textEntryId = new TextEntryId(chatId, vLocalId, AssumeValid.Option);
            textEntry = await this
                .GetEntry(session,textEntryId, cancellationToken)
                .Require(ChatEntry.MustNotBeRemoved)
                .ConfigureAwait(false);

            // Check constraints
            if (textEntry.AuthorId != author.Id)
                throw StandardError.Unauthorized("You can edit only your own messages.");
            if (textEntry.Kind != ChatEntryKind.Text || textEntry.IsStreaming || textEntry.AudioEntryId.HasValue)
                throw StandardError.Constraint("Only text messages can be edited.");

            textEntry = textEntry with { Content = text };
            if (repliedChatEntryId.IsSome(out var v))
                textEntry = textEntry with { RepliedEntryLocalId = v };
            var upsertCommand = new ChatsBackend_UpsertEntry(textEntry);
            textEntry = await Commander.Call(upsertCommand, cancellationToken).ConfigureAwait(false);
        }
        else {
            // Create
            var textEntryId = new TextEntryId(chatId, 0, AssumeValid.Option);
            var upsertCommand = new ChatsBackend_UpsertEntry(
                new ChatEntry(textEntryId) {
                    AuthorId = author.Id,
                    Content = text,
                    RepliedEntryLocalId = repliedChatEntryId.IsSome(out var v) ? v : null,
                },
                command.Attachments.Count > 0);
            textEntry = await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
            textEntryId = textEntry.Id.ToTextEntryId();

            for (var index = 0; index < command.Attachments.Count; index++) {
                var attachment = new TextEntryAttachment {
                    EntryId = textEntryId,
                    Index = index,
                    MediaId = command.Attachments[index],
                };
                var createAttachmentCommand = new ChatsBackend_CreateAttachment(attachment);
                await Commander.Call(createAttachmentCommand, true, cancellationToken).ConfigureAwait(false);
            }
        }

        return textEntry;
    }

    // [CommandHandler]
    public virtual async Task OnRemoveTextEntry(Chats_RemoveTextEntry command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, localId) = command;
        var author = await Authors.EnsureJoined(session, chatId, cancellationToken).ConfigureAwait(false);
        var chat = await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(ChatPermissions.Write);

        var textEntryId = new TextEntryId(chatId, localId, AssumeValid.Option);
        var textEntry = await this
            .GetEntry(session, textEntryId, cancellationToken)
            .Require(ChatEntry.MustNotBeRemoved)
            .ConfigureAwait(false);

        // Check constraints
        if (textEntry.AuthorId != author.Id)
            throw StandardError.Unauthorized("You can remove only your own messages.");
        if (textEntry.IsStreaming)
            throw StandardError.Constraint("This entry is still recording, you'll be able to remove it later.");

        await Remove(textEntryId).ConfigureAwait(false);
        if (textEntry.AudioEntryId is { } localAudioEntryId) {
            var audioEntryId = new ChatEntryId(chatId, ChatEntryKind.Audio, localAudioEntryId, AssumeValid.Option);
            await Remove(audioEntryId).ConfigureAwait(false);
        }

        async Task Remove(ChatEntryId entryId1)
        {
            var entry1 = await this.GetEntry(session, entryId1, cancellationToken).ConfigureAwait(false);
            if (entry1 == null || entry1.IsRemoved)
                return;

            entry1 = entry1 with { IsRemoved = true };
            var upsertCommand = new ChatsBackend_UpsertEntry(entry1);
            await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        }
    }

    // [CommandHandler]
    public virtual async Task<Chat> OnGetOrCreateFromTemplate(Chats_GetOrCreateFromTemplate command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, templateChatId) = command;
        var templateChat = await Get(session, templateChatId, cancellationToken).ConfigureAwait(false);
        templateChat.Require(Chat.MustBeTemplate);

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var chat = await Backend.GetTemplatedChatFor(templateChatId, account.Id, cancellationToken).ConfigureAwait(false);
        if (chat != null)
            return chat;

        var templateAuthorIds = await AuthorsBackend.ListAuthorIds(templateChatId, cancellationToken).ConfigureAwait(false);
        var templateAuthors = await templateAuthorIds
            .Select(aId => AuthorsBackend.Get(templateChatId, aId, cancellationToken))
            .Collect(4)
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
            .Collect(4)
            .ConfigureAwait(false);
        var templateOwner = authorRoles.FirstOrDefault(x => x.Roles.Any(r => r.SystemRole == SystemRole.Owner));

        // clone template chat
        var cloneCommand = new ChatsBackend_Change(
            ChatId.None,
            null,
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
            templateAuthors.Single(a => a?.Id == templateOwner.AuthorId)?.UserId ?? UserId.None // Owner is mandatory
        );

        var cloned = await Commander.Call(cloneCommand, cancellationToken).ConfigureAwait(false);
        var chatId = cloned.Id;

        // copy existing template authors and their roles
        var clonedAuthors = new List<AuthorFull>();
        foreach (var templateAuthor in templateAuthors.Where(ta => ta != null)) {
            var cloneAuthorCommand = new AuthorsBackend_Upsert(
                chatId,
                AuthorId.None,
                templateAuthor!.UserId,
                ExpectedVersion: null,
                new AuthorDiff {
                    AvatarId = templateAuthor.AvatarId,
                },
                DoNotNotify: true);
            var clonedAuthor = await Commander.Call(cloneAuthorCommand, cancellationToken).ConfigureAwait(false);
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
                    return (tuples.FirstOrDefault().Role, AuthorIds: tuples.Select(x => authorMap[x.AuthorId]).ToApiArray());
                })
            .ToList();

        foreach (var (role, roleAuthorIds) in roleAuthors) {
            var createOwnersRoleCmd = new RolesBackend_Change(cloned.Id, default, null, new() {
                Create = new RoleDiff {
                    Picture = role.Picture,
                    Name = role.Name,
                    SystemRole = role.SystemRole,
                    Permissions = role.Permissions,
                    AuthorIds = new SetDiff<ApiArray<AuthorId>, AuthorId> {
                        AddedItems = roleAuthorIds,
                    },
                },
            });
            await Commander.Call(createOwnersRoleCmd, cancellationToken).ConfigureAwait(false);
        }

        // join guest author
        var avatarIds = await Avatars.ListOwnAvatarIds(session, cancellationToken).ConfigureAwait(false);
        var avatars = await avatarIds
            .Select(aId => Avatars.GetOwn(session, aId, cancellationToken))
            .Collect().ConfigureAwait(false);
        var guestAvatar = avatars
            .Where(a => a != null)
            .FirstOrDefault(a => OrdinalEquals(a!.Name, Avatar.GuestName));
        if (guestAvatar == null) {
            var createAvatarCommand = new Avatars_Change(session, Symbol.Empty, null, new Change<AvatarFull> {
                Create = new AvatarFull(account.Id) {
                    Name = "Guest",
                }.WithMissingPropertiesFrom(account.Avatar),
            });
            var newAvatar = await Commander.Call(createAvatarCommand, cancellationToken).ConfigureAwait(false);
            guestAvatar = newAvatar;
        }
        var createAuthorCommand = new AuthorsBackend_Upsert(
            chatId,
            AuthorId.None,
            account.Id,
            ExpectedVersion: null,
            new AuthorDiff {
                AvatarId = guestAvatar.Id,
            });
        await Commander.Run(createAuthorCommand, cancellationToken).ConfigureAwait(false);

        return cloned;
    }

    // Private methods

    public virtual async Task<PrincipalId> GetOwnPrincipalId(
        Session session, ChatId chatId,
        CancellationToken cancellationToken)
    {
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author != null)
            return new PrincipalId(author.Id, AssumeValid.Option);

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return new PrincipalId(account.Id, AssumeValid.Option);
    }
}
