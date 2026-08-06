using ActualChat.Invite;

namespace ActualChat.Testing.Host;

public static class ChatOperations
{
    private const string DefaultChatTitle = "test chat";

    public static async Task<(Chat.Chat Chat, Symbol InviteId)> CreateAndGetChat(
        this IWebTester tester,
        bool isPublicChat,
        string title = DefaultChatTitle,
        PlaceId? placeId = null,
        CancellationToken cancellationToken = default)
    {
        var (id, inviteId) = await CreateChat(tester,
            c => c with { IsPublic = isPublicChat, Title = title, PlaceId = placeId, Kind = null }, cancellationToken);
        var chat = await tester.Chats.Get(tester.Session, id, CancellationToken.None).Require();
        return (chat, inviteId);
    }

    public static Task<(ChatId, Symbol)> CreateChat(
        this IWebTester tester,
        bool isPublicChat,
        string title = "",
        PlaceId? placeId = null,
        CancellationToken cancellationToken = default)
        => CreateChat(tester,
            c => c with {
                IsPublic = isPublicChat,
                Title = title.NullIfEmpty() ?? tester.Out.GetTest()?.DisplayName.NullIfEmpty() ?? DefaultChatTitle,
                PlaceId = placeId, Kind = null
            },
            cancellationToken);

    public static async Task<(ChatId, Symbol)> CreateChat(this IWebTester tester, Func<ChatDiff, ChatDiff> configure, CancellationToken cancellationToken = default)
    {
        var session = tester.Session;
        var chatDiff = configure(new ChatDiff() {
            Title = DefaultChatTitle,
            Kind = ChatKind.Group,
        });
        var isPublicChat = chatDiff.IsPublic ?? false;

        var commander = tester.Commander;
        var chat = await commander.Call(new Chats_Change(session,
            default,
            null,
            new () {
                Create = chatDiff,
            }), cancellationToken);
        chat.Require();
        var chatId = chat.Id;

        var inviteId = Symbol.Empty;
        if (!isPublicChat) {
            // to join private chat we need to generate invite code
            Invite.Invite invite = Invite.ChatInvite.New(Constants.Invites.Defaults.ChatRemaining, chatId);
            invite = await commander.Call(new Invites_Generate(session, invite), cancellationToken);
            inviteId = invite.Id;
        }

        return (chatId, inviteId);
    }

    public static Task<Chat.Chat> UpdateChat(this IWebTester tester, ChatId chatId, string title)
        => tester.Commander.Call(new Chats_Change(tester.Session,
            chatId,
            null,
            Change.Update(new ChatDiff {
                Title = title
            })));

    public static Task<Chat.Chat> SetChatMedia(this IWebTester tester, ChatId chatId, MediaId mediaId)
        => tester.Commander.Call(new Chats_Change(tester.Session,
            chatId,
            null,
            Change.Update(new ChatDiff {
                MediaId = mediaId
            })));

    public static Task<Chat.Chat> DeleteChat(this IWebTester tester, ChatId chatId)
        => tester.Commander.Call(new Chats_Change(tester.Session, chatId, null, Change.Remove(new ChatDiff())));

    public static async Task<AuthorFull> JoinChat(this IWebTester tester, ChatId chatId, Symbol inviteId,
        bool? joinAnonymously = null, Symbol avatarId = default)
    {
        var session = tester.Session;
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();

        var chat = await chats.Get(session, chatId, default).ConfigureAwait(false);
        var chatRules = await chats.GetRules(session, chatId, default).ConfigureAwait(false);
        var canJoin = chatRules.CanJoin();
        var isPublicChat = chat is { IsPublic: true };

        if (!isPublicChat) {
            canJoin.Should().BeFalse();
            // to join private chat we need to activate invite code first
            await commander.Call(new Invites_Use(session, inviteId), true);

            var c = await Computed.Capture(() => chats.GetRules(session, chatId, default));
            c = await c.When(x => x.CanJoin()).WaitAsync(TimeSpan.FromSeconds(3));
            canJoin = c.Value.CanJoin();
        }

        canJoin.Should().BeTrue();

        var command = new Authors_Join(session, chatId, AvatarId: avatarId, JoinAnonymously: joinAnonymously);
        var author = await commander.Call(command, true).ConfigureAwait(false);
        return author;
    }

    public static async Task AssertJoined(this IWebTester tester, ChatId chatId)
    {
        var services = tester.AppServices;
        var session = tester.Session;
        var account = await services.GetRequiredService<IAccounts>().GetOwn(session, default).ConfigureAwait(false);
        var chats = services.GetRequiredService<IChats>();

        var cRules = await Computed.Capture(() => chats.GetRules(session, chatId, default));
        await cRules.When(r => r.CanWrite()).WaitAsync(TimeSpan.FromSeconds(3));
        var rules = await cRules.Use();
        rules.CanRead().Should().BeTrue();
        rules.CanWrite().Should().BeTrue();

        var chat = await chats.Get(session, chatId, default).ConfigureAwait(false);
        chat.Should().NotBeNull();
        var authors = services.GetRequiredService<IAuthors>();
        var author = await authors.GetOwn(session, chatId, default).ConfigureAwait(false);
        author.Should().NotBeNull();
        author!.UserId.Should().Be(account.Id);
        author.HasLeft.Should().BeFalse();

        var authorsUpgradeBackend = services.GetRequiredService<IAuthorsUpgradeBackend>();
        var ownChatIds = await authorsUpgradeBackend.ListOwnChatIds(session, default).ConfigureAwait(false);
        ownChatIds.Should().Contain(chatId);

        var userIds = await authors.ListUserIds(session, chatId, default).ConfigureAwait(false);
        userIds.Should().Contain(account.Id);
        var authorIds = await authors.ListAuthorIds(session, chatId, default).ConfigureAwait(false);
        authorIds.Should().Contain(author.Id);
    }

    public static async Task<AuthorFull> InviteToChat(this IWebTester tester, ChatId chatId, UserId userId)
    {
        var authors = await tester.InviteToChat(chatId, [userId]);
        return authors[0];
    }

    public static  Task<AuthorFull[]> InviteToChat(this IWebTester tester, ChatId chatId, params IReadOnlyCollection<Account> users)
        => tester.InviteToChat(chatId, users.Select(x => x.Id).ToArray());

    public static async Task<AuthorFull[]> InviteToChat(this IWebTester tester, ChatId chatId, params UserId[] userIds)
    {
        var session = tester.Session;
        var commander = tester.Commander;

        await commander.Call(new Authors_Invite(session, chatId, userIds));

        // TODO: return author from command
        var authorIds = await tester.Authors.ListAuthorIds(tester.Session, chatId, CancellationToken.None);
        return await userIds
            .Select(userId
                => tester.AuthorsBackend
                    .GetByUserId(chatId, userId, RequestedAuthorKind.Full, CancellationToken.None)
                    .Require())
            .Collect(Environment.ProcessorCount / 2);
    }

    public static Task LeaveChat(this IWebTester tester, ChatId chatId)
        => tester.Commander.Call(new Authors_Leave(tester.Session, chatId));
}
