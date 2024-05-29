﻿using ActualChat.Chat;
using ActualChat.Invite;
using ActualChat.Users;

namespace ActualChat.Testing.Host;

public static class ChatOperations
{
    private const string DefaultChatTitle = "test chat";
    private const string DefaultPlaceTitle = "test place";

    public static Task<(ChatId, Symbol)> CreateChat(
        this IWebTester tester,
        bool isPublicChat,
        string title = DefaultChatTitle,
        PlaceId? placeId = null)
        => CreateChat(tester, c => c with { IsPublic = isPublicChat, Title = title, PlaceId = placeId, Kind = null });

    public static async Task<(ChatId, Symbol)> CreateChat(this IWebTester tester, Func<ChatDiff, ChatDiff> configure)
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
            }));
        chat.Require();
        var chatId = chat.Id;

        var inviteId = Symbol.Empty;
        if (!isPublicChat) {
            // to join private chat we need to generate invite code
            var invite = Invite.Invite.New(Constants.Invites.Defaults.ChatRemaining, new ChatInviteOption(chatId));
            invite = await commander.Call(new Invites_Generate(session, invite));
            inviteId = invite.Id;
        }

        return (chatId, inviteId);
    }

    public static Task<(PlaceId, Symbol)> CreatePlace(this IWebTester tester, bool isPublicPlace, string title = DefaultPlaceTitle)
        => CreatePlace(tester, c => c with { IsPublic = isPublicPlace });

    public static async Task<(PlaceId, Symbol)> CreatePlace(this IWebTester tester, Func<PlaceDiff, PlaceDiff> configure)
    {
        var session = tester.Session;
        var placeDiff = configure(new PlaceDiff() {
            Title = DefaultPlaceTitle,
        });
        var isPublicPlace = placeDiff.IsPublic ?? false;

        var commander = tester.Commander;
        var place = await commander.Call(new Places_Change(session,
            default,
            null,
            new () {
                Create = placeDiff,
            }));
        place.Require();
        var placeId = place.Id;

        var inviteId = Symbol.Empty;
        if (!isPublicPlace) {
            // TODO(DF): Somehow activate possibility to join place. Invite code?
            // var invite = Invite.Invite.New(Constants.Invites.Defaults.ChatRemaining, new ChatInviteOption(placeId));
            // invite = await commander.Call(new Invites_Generate(session, invite));
            // inviteId = invite.Id;
        }

        return (placeId, inviteId);
    }

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

    public static async Task InviteToPlace(this IWebTester tester, PlaceId placeId, params UserId[] userIds)
    {
        var session = tester.Session;
        var commander = tester.Commander;
        await commander.Call(new Places_Invite(session, placeId, userIds));
    }

    public static Task InviteToPlace(this IWebTester tester, PlaceId placeId, params Account[] accounts)
        => tester.InviteToPlace(placeId, accounts.Select(x => x.Id).ToArray());

    public static async Task InviteToChat(this IWebTester tester, ChatId chatId, params UserId[] userIds)
    {
        var session = tester.Session;
        var commander = tester.Commander;

        await commander.Call(new Authors_Invite(session, chatId, userIds));
    }
}
