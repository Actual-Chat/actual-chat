using ActualChat.Queues;
using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public sealed class NotificationHistoryTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private INotificationsBackend Backend => Tester.NotificationsBackend;

    [Fact]
    public async Task MentionShouldBeLoggedAndSurviveRead()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false, "Notification history chat");
        var aliceAuthor = await Tester.InviteToChat(chatId, alice.Id);

        // act
        var entry = await Tester.CreateTextEntry(chatId, $"hi @a:{aliceAuthor.Id}");

        // assert
        NotificationHistoryItem item = null!;
        await TestExt.When(async () => {
            var items = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
            item = items.Should().ContainSingle(x => x.Kind == NotificationKind.Mention).Subject;
        }, WaitTimeout);
        item.ChatId.Should().Be(chatId);
        item.EntryId.Should().Be(entry.Id);
        item.AuthorId.Should().Be(entry.AuthorId);
        item.Seq.Should().BePositive();

        await Commander.Call(new ChatPositionsBackend_Set(
            alice.Id, chatId, ChatPositionKind.Read, new ChatPosition(entry.LocalId)));
        await TestExt.When(async () => {
            var info = await Backend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Items.Should().NotContain(n => n.Kind == NotificationKind.Mention,
                "reading the chat clears the active mention");
        }, WaitTimeout);
        var after = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
        after.Should().ContainSingle(x => x.Kind == NotificationKind.Mention,
            "the log is not the active set: a read must not erase history");
    }

    [Fact]
    public async Task MessageKindShouldNotBeLogged()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var chatId = ChatId.Parse("the-actual-one");
        var authorId = AuthorId.New(chatId, 1);
        var now = Clocks.SystemClock.Now;
        var message = MessageNotification.New(alice.Id, chatId, 5, authorId) with {
            SentAt = now, Title = "Chat", Text = "plain traffic",
        };
        var mention = MentionNotification.New(alice.Id, ChatEntryId.New(chatId, 6), authorId) with {
            SentAt = now, Title = "Chat", Text = "@you",
        };

        // act
        await Queues.Enqueue(new UserNotifiedEvent(message));
        await Queues.Enqueue(new UserNotifiedEvent(mention));

        // assert
        await TestExt.When(async () => {
            var items = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
            items.Should().ContainSingle(x => x.Kind == NotificationKind.Mention);
        }, WaitTimeout);
        await Queues.WhenProcessing();
        var all = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
        all.Should().NotContain(x => x.Kind == NotificationKind.Message, "per-chat traffic is not addressed to anyone");
    }

    [Fact]
    public async Task ConversationShouldBeLoggedWithItsStartEntry()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var chatId = ChatId.Parse("the-actual-one");
        var conversationId = ConversationId.New(chatId, 11);
        var conversation = ConversationNotification.New(alice.Id, conversationId, 15) with {
            SentAt = Clocks.SystemClock.Now, Title = "Chat", Text = "live talk",
        };

        // act
        await Queues.Enqueue(new UserNotifiedEvent(conversation));

        // assert
        NotificationHistoryItem item = null!;
        await TestExt.When(async () => {
            var items = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
            item = items.Should().ContainSingle(x => x.Kind == NotificationKind.Conversation).Subject;
        }, WaitTimeout);
        item.ChatId.Should().Be(chatId);
        item.EntryId.Should().Be(ChatEntryId.New(chatId, 11),
            "a conversation carries no entry of its own, so its row anchors where it started");
    }

    [Fact]
    public async Task ListHistoryShouldBeScopedToOneUser()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var bob = await Tester.SignInAsUniqueBob();
        var chatId = ChatId.Parse("the-actual-one");
        var authorId = AuthorId.New(chatId, 1);
        var now = Clocks.SystemClock.Now;
        var aliceMention = MentionNotification.New(alice.Id, ChatEntryId.New(chatId, 21), authorId) with {
            SentAt = now, Title = "Chat", Text = "@alice",
        };
        var bobMention = MentionNotification.New(bob.Id, ChatEntryId.New(chatId, 22), authorId) with {
            SentAt = now, Title = "Chat", Text = "@bob",
        };

        // act
        await Queues.Enqueue(new UserNotifiedEvent(aliceMention));
        await Queues.Enqueue(new UserNotifiedEvent(bobMention));

        // assert
        await TestExt.When(async () => {
            var mine = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
            var theirs = await Backend.ListHistory(bob.Id, new NotificationHistoryQuery(), CancellationToken.None);
            mine.Should().ContainSingle();
            theirs.Should().ContainSingle();
        }, WaitTimeout);
        var aliceItems = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
        var bobItems = await Backend.ListHistory(bob.Id, new NotificationHistoryQuery(), CancellationToken.None);
        aliceItems.Select(x => x.Text).Should().Equal(["@alice"], "the log is scoped to its own user");
        bobItems.Select(x => x.Text).Should().Equal(["@bob"], "the log is scoped to its own user");
    }

    [Fact]
    public async Task RedeliveredEventShouldNotDuplicateTheRow()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var chatId = ChatId.Parse("the-actual-one");
        var mention = MentionNotification.New(alice.Id, ChatEntryId.New(chatId, 7), AuthorId.New(chatId, 1)) with {
            SentAt = Clocks.SystemClock.Now, Title = "Chat", Text = "@you",
        };

        // act
        await Queues.Enqueue(new UserNotifiedEvent(mention));
        await Queues.Enqueue(new UserNotifiedEvent(mention));

        // assert
        await TestExt.When(async () => {
            var items = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
            items.Should().ContainSingle();
        }, WaitTimeout);
        await Queues.WhenProcessing();
        var all = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
        all.Should().ContainSingle(
            "the same notification with the same SentAt is one row however often it is delivered");
    }

    [Fact]
    public async Task ListHistoryShouldFilterByKindAndWalkByCursor()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var notifications = AppHost.Services.GetRequiredService<INotifications>();
        var chatId = ChatId.Parse("the-actual-one");
        var authorId = AuthorId.New(chatId, 1);
        var now = Clocks.SystemClock.Now;
        var expectedKinds = new List<NotificationKind>();
        for (var lid = 1; lid <= 5; lid++) {
            var entryId = ChatEntryId.New(chatId, lid);
            Notification n = lid % 2 == 0
                ? ReactionNotification.New(alice.Id, entryId, authorId)
                : MentionNotification.New(alice.Id, entryId, authorId);
            n = n with { SentAt = now + TimeSpan.FromMilliseconds(lid), Title = "Chat", Text = $"#{lid}" };
            expectedKinds.Add(n.Kind);
            await Queues.Enqueue(new UserNotifiedEvent(n));
        }
        await TestExt.When(async () => {
            var items = await notifications.ListHistory(Tester.Session,
                new NotificationHistoryQuery(), CancellationToken.None);
            items.Should().HaveCount(5);
        }, WaitTimeout);

        // act
        var all = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery(), CancellationToken.None);
        var reactions = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { Kinds = ApiArray.New(NotificationKind.Reaction) }, CancellationToken.None);
        var unlogged = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { Kinds = ApiArray.New(NotificationKind.Message) }, CancellationToken.None);
        var page1 = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { Limit = 2 }, CancellationToken.None);
        var page2 = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { Limit = 2, AfterSeq = page1[^1].Seq }, CancellationToken.None);
        var page3 = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { Limit = 2, AfterSeq = page2[^1].Seq }, CancellationToken.None);
        var newest = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { IsNewestFirst = true }, CancellationToken.None);
        var olderThanNewest = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { IsNewestFirst = true, AfterSeq = newest[0].Seq }, CancellationToken.None);

        // assert
        all.Select(x => x.Text).Should().BeEquivalentTo(["#1", "#2", "#3", "#4", "#5"],
            "all five notifications are logged, though insertion order can race with enqueue order "
            + "since the queue processes one shard's events concurrently");
        all.Select(x => x.Kind).Should().BeEquivalentTo(expectedKinds);
        all.Select(x => x.Seq).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        reactions.Should().HaveCount(2).And.OnlyContain(x => x.Kind == NotificationKind.Reaction);
        unlogged.Should().BeEmpty("Message is never logged, so filtering on it matches nothing");
        page1.Concat(page2).Concat(page3).Select(x => x.Seq).Should().Equal(all.Select(x => x.Seq),
            "cursor pages tile the log without overlap or gaps");
        page3.Should().ContainSingle();
        newest.Select(x => x.Seq).Should().Equal(all.Select(x => x.Seq).Reverse());
        olderThanNewest.Select(x => x.Seq).Should().Equal(newest.Skip(1).Select(x => x.Seq),
            "with newest-first the cursor continues to older rows");
    }

    [Fact]
    public async Task HistoryVersionShouldGrowOnInsertAndNotOnRedelivery()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var chatId = ChatId.Parse("the-actual-one");
        var mention = MentionNotification.New(alice.Id, ChatEntryId.New(chatId, 21), AuthorId.New(chatId, 1)) with {
            SentAt = Clocks.SystemClock.Now, Title = "Chat", Text = "@you",
        };
        var before = await Backend.GetHistoryVersion(alice.Id, CancellationToken.None);

        // act
        await Queues.Enqueue(new UserNotifiedEvent(mention));
        long afterFirst = 0;
        await TestExt.When(async () => {
            afterFirst = await Backend.GetHistoryVersion(alice.Id, CancellationToken.None);
            afterFirst.Should().BeGreaterThan(before, "a logged notification bumps the version");
        }, WaitTimeout);
        await Queues.Enqueue(new UserNotifiedEvent(mention));
        await Queues.WhenProcessing();

        // assert
        var afterSecond = await Backend.GetHistoryVersion(alice.Id, CancellationToken.None);
        afterSecond.Should().Be(afterFirst, "a redelivered event inserts nothing, so nothing to invalidate");
        var items = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
        items.Should().ContainSingle().Which.NotificationId.Should().Be(mention.Id);
        afterFirst.Should().Be(items[0].Seq, "the version is the newest row's Seq");
    }
}
