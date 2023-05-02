﻿using ActualChat.App.Server;
using ActualChat.Invite;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Chat.IntegrationTests;

public static class ChatOperations
{
    public static async Task<(ChatId, Symbol)> CreateChat(AppHost appHost, bool isPublicChat)
    {
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignIn(new User("", "Alice"));

        var commander = tester.Commander;
        var chat = await commander.Call(new IChats.ChangeCommand(session,
            default,
            null,
            new () {
                Create = new ChatDiff() {
                    Title = "test chat",
                    Kind = ChatKind.Group,
                    IsPublic = isPublicChat,
                },
            }));
        chat.Require();
        var chatId = chat.Id;

        var inviteId = Symbol.Empty;
        if (!isPublicChat) {
            // to join private chat we need to generate invite code
            var invite = new Invite.Invite {
                Remaining = 10,
                Details = new ChatInviteOption(chatId),
            };
            invite = await commander.Call(new IInvites.GenerateCommand(session, invite));
            inviteId = invite.Id;
        }

        return (chatId, inviteId);
    }

    public static async Task<AuthorFull> JoinChat(IWebTester tester, ChatId chatId, Symbol inviteId,
        bool? joinAnonymously = null, Symbol avatarId = default)
    {
        var session = tester.Session;
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();

        var chat = await chats.Get(session, chatId, default).ConfigureAwait(false);
        var chatRules = await chats.GetRules(session, chatId, default).ConfigureAwait(false);
        var canJoin = chatRules.CanJoin();
        var isPublicChat = chat != null && chat.IsPublic;

        if (!isPublicChat) {
            canJoin.Should().BeFalse();
            // to join private chat we need to activate invite code first
            await commander.Call(new IInvites.UseCommand(session, inviteId), true);

            var c = await Computed.Capture(() => chats.GetRules(session, chatId, default));
            c = await c.When(x => x.CanJoin()).WaitAsync(TimeSpan.FromSeconds(3));
            canJoin = c.Value.CanJoin();
        }

        canJoin.Should().BeTrue();

        var command = new IAuthors.JoinCommand(session, chatId, AvatarId: avatarId, JoinAnonymously: joinAnonymously);
        var author = await commander.Call(command, true).ConfigureAwait(false);
        return author;
    }

    public static async Task AssertJoined(IWebTester tester, ChatId chatId)
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
}
