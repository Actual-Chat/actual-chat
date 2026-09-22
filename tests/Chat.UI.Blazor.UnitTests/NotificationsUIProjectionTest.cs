using ActualChat.Hosting;
using ActualChat.Notifications;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Hosting;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class NotificationsUIProjectionTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ChatId ChatA = ChatId.Parse("the-actual-one");
    private static readonly ChatId ChatB = ChatId.Parse(GroupChatId.New().Value);
    private static readonly ChatId PeerChat = PeerChatId.New(TestUserId, UserId.New());

    [Fact]
    public async Task ListByKindShouldFilterAndSortNewestFirst()
    {
        // arrange
        var older = NewReaction(1, Moment.EpochStart + TimeSpan.FromSeconds(1));
        var newer = NewReaction(2, Moment.EpochStart + TimeSpan.FromSeconds(2));
        var mention = MentionNotification.New(TestUserId, ChatEntryId.New(ChatA, 3));
        await using var scope = NewScope(older, mention, newer);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var result = await notificationsUI.ListByKind(NotificationKind.Reaction);

        // assert
        result.Select(x => x.Id).Should().Equal([newer.Id, older.Id]);
    }

    [Fact]
    public async Task ReactionStateShouldTakeNewestReactionForChat()
    {
        // arrange
        var older = NewReaction(1, Moment.EpochStart + TimeSpan.FromSeconds(1)) with {
            Emojis = ApiArray.New(Emojis.Awesome),
        };
        var newer = NewReaction(2, Moment.EpochStart + TimeSpan.FromSeconds(2)) with {
            Emojis = ApiArray.New(Emojis.Party),
        };
        await using var scope = NewScope(older, newer);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var state = await notificationsUI.GetReactionState(ChatA);

        // assert
        state.Emoji.Should().Be(Emojis.Party);
        state.SentAt.Should().Be(newer.SentAt);
    }

    [Fact]
    public async Task ReactionStateShouldPreferLastEmojiOverAccumulatedOrder()
    {
        // arrange - an out-of-order merge leaves the newest emoji in the middle of the accumulated set
        var notification = NewReaction(1, Moment.EpochStart + TimeSpan.FromSeconds(2)) with {
            Emojis = ApiArray.New(Emojis.Party, Emojis.Awesome),
            LastEmoji = Emojis.Party,
        };
        await using var scope = NewScope(notification);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var state = await notificationsUI.GetReactionState(ChatA);

        // assert
        state.Emoji.Should().Be(Emojis.Party);
    }

    [Fact]
    public async Task ReactionStateShouldFallBackToAccumulatedSetWithoutLastEmoji()
    {
        // arrange - a notification persisted before LastEmoji existed
        var notification = NewReaction(1, Moment.EpochStart + TimeSpan.FromSeconds(1)) with {
            Emojis = ApiArray.New(Emojis.Awesome, Emojis.Party),
        };
        await using var scope = NewScope(notification);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var state = await notificationsUI.GetReactionState(ChatA);

        // assert
        state.Emoji.Should().Be(Emojis.Party);
    }

    [Fact]
    public async Task ReactionStateShouldBeDefaultForChatWithoutReactions()
    {
        // arrange
        await using var scope = NewScope();
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var state = await notificationsUI.GetReactionState(ChatA);

        // assert
        state.Should().Be(default(ChatReactionState));
    }

    [Fact]
    public async Task ChatReactionsShouldFilterByChatAndSortByEntry()
    {
        // arrange
        var later = NewReaction(5, Moment.EpochStart + TimeSpan.FromSeconds(1));
        var earlier = NewReaction(2, Moment.EpochStart + TimeSpan.FromSeconds(2));
        var otherChat = ReactionNotification.New(TestUserId, ChatEntryId.New(ChatB, 1));
        var mention = MentionNotification.New(TestUserId, ChatEntryId.New(ChatA, 3));
        await using var scope = NewScope(later, otherChat, mention, earlier);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var result = await notificationsUI.ListChatReactions(ChatA);

        // assert
        result.Select(x => x.EntryLid).Should().Equal([2, 5]);
    }

    [Fact]
    public async Task AttentionAtShouldTakeNewestPingForChat()
    {
        // arrange
        var older = NewAttention(ChatA, 1, Moment.EpochStart + TimeSpan.FromSeconds(1));
        var newer = NewAttention(ChatA, 2, Moment.EpochStart + TimeSpan.FromSeconds(2));
        var otherChatPing = NewAttention(ChatB, 1, Moment.EpochStart + TimeSpan.FromSeconds(3));
        await using var scope = NewScope(older, otherChatPing, newer);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var attentionAt = await notificationsUI.GetAttentionAt(ChatA);

        // assert
        attentionAt.Should().Be(newer.SentAt);
    }

    [Fact]
    public async Task NavigationTargetShouldPreferAPingOverAMentionOverAReaction()
    {
        // arrange
        var reaction = NewReaction(9, Moment.EpochStart + TimeSpan.FromSeconds(1));
        var mention = MentionNotification.New(TestUserId, ChatEntryId.New(ChatA, 4));
        var ping = NewAttention(ChatA, 7, Moment.EpochStart);
        await using var scope = NewScope(reaction, mention, ping);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var target = await notificationsUI.GetNavigationTarget(ChatA, includeReactions: true);

        // assert
        target!.Id.Should().Be(ping.Id, "a ping is the only ringer among the three");
    }

    [Fact]
    public async Task NavigationTargetShouldWalkOneKindForward()
    {
        // arrange
        var later = MentionNotification.New(TestUserId, ChatEntryId.New(ChatA, 8));
        var earlier = MentionNotification.New(TestUserId, ChatEntryId.New(ChatA, 4));
        await using var scope = NewScope(later, earlier);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var target = await notificationsUI.GetNavigationTarget(ChatA, includeReactions: true);

        // assert
        target!.EntryId.LocalId.Should().Be(4, "the walk moves forward through the chat");
    }

    [Fact]
    public async Task NavigationTargetShouldSkipOtherChatsAndChatCoalescingKinds()
    {
        // arrange - a message notification anchors at the first unread entry, which is where the
        // row's plain chat link already lands
        var message = MessageNotification.New(TestUserId, ChatA, 2, AuthorId.New(ChatA, 1));
        var otherChatMention = MentionNotification.New(TestUserId, ChatEntryId.New(ChatB, 5));
        var mention = MentionNotification.New(TestUserId, ChatEntryId.New(ChatA, 3));
        await using var scope = NewScope(message, otherChatMention, mention);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var target = await notificationsUI.GetNavigationTarget(ChatA, includeReactions: true);

        // assert
        target!.Id.Should().Be(mention.Id);
    }

    [Fact]
    public async Task NavigationTargetShouldSkipReactionsForTheMentionsTab()
    {
        // arrange
        var reaction = NewReaction(9, Moment.EpochStart) with { Emojis = ApiArray.New(Emojis.Awesome) };
        await using var scope = NewScope(reaction);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var withReactions = await notificationsUI.GetNavigationTarget(ChatA, includeReactions: true);
        var withoutReactions = await notificationsUI.GetNavigationTarget(ChatA, includeReactions: false);

        // assert
        withReactions!.Emoji.Should().Be(Emojis.Awesome);
        withoutReactions.Should().BeNull("reactions lift a chat onto the other tabs, not this one");
    }

    [Fact]
    public async Task AttentionAtShouldBeNullForChatWithoutPings()
    {
        // arrange
        await using var scope = NewScope(NewReaction(1, Moment.EpochStart + TimeSpan.FromSeconds(1)));
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var attentionAt = await notificationsUI.GetAttentionAt(ChatA);

        // assert
        attentionAt.Should().BeNull();
    }

    [Fact]
    public async Task ListHistoryShouldGroupPerChatNewestFirstWithOlderCount()
    {
        // arrange
        var a1 = NewHistory(NotificationKind.Mention, ChatA, 1, 1);
        var b1 = NewHistory(NotificationKind.Reaction, ChatB, 1, 2);
        var a2 = NewHistory(NotificationKind.Attention, ChatA, 2, 3);
        var a3 = NewHistory(NotificationKind.Mention, ChatA, 3, 4);
        await using var scope = NewScope([], [a1, b1, a2, a3]);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var groups = await notificationsUI.ListHistory(ChatListFilter.Unread.Id);

        // assert
        groups.Select(g => g.ChatId).Should().Equal([ChatA, ChatB], "newest group first");
        groups[0].Newest.Should().Be(a3);
        groups[0].OlderCount.Should().Be(2);
        groups[1].Newest.Should().Be(b1);
        groups[1].OlderCount.Should().Be(0);
    }

    [Fact]
    public async Task ListHistoryShouldMapTabsToKinds()
    {
        // arrange
        var mention = NewHistory(NotificationKind.Mention, ChatA, 1, 1);
        var attention = NewHistory(NotificationKind.Attention, ChatB, 1, 2);
        var reaction = NewHistory(NotificationKind.Reaction, ChatA, 2, 3);
        var peerReply = NewHistory(NotificationKind.Reply, PeerChat, 1, 4);
        await using var scope = NewScope([], [mention, attention, reaction, peerReply]);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var all = await notificationsUI.ListHistory(ChatListFilter.Unread.Id);
        var people = await notificationsUI.ListHistory(ChatListFilter.UnreadPeople.Id);
        var mentions = await notificationsUI.ListHistory(ChatListFilter.UnreadMentions.Id);
        var reactions = await notificationsUI.ListHistory(NotificationsUI.ReactionsFilterId);

        // assert
        all.Select(g => g.ChatId).Should().BeEquivalentTo([ChatA, ChatB, PeerChat]);
        people.Select(g => g.ChatId).Should().Equal([PeerChat], "People keeps peer chats only");
        mentions.Select(g => g.Newest.Kind).Should()
            .OnlyContain(k => k == NotificationKind.Mention || k == NotificationKind.Attention);
        mentions.Should().HaveCount(2);
        reactions.Should().ContainSingle().Which.Newest.Should().Be(reaction);
    }

    [Fact]
    public async Task ListHistoryShouldHideNotificationsThatAreStillActive()
    {
        // arrange
        var activeMention = MentionNotification.New(TestUserId, ChatEntryId.New(ChatA, 5));
        var stillActive = NewHistory(NotificationKind.Mention, ChatA, 5, 2);
        var older = NewHistory(NotificationKind.Mention, ChatA, 4, 1);
        await using var scope = NewScope([activeMention], [older, stillActive]);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var groups = await notificationsUI.ListHistory(ChatListFilter.UnreadMentions.Id);

        // assert
        var group = groups.Should().ContainSingle().Subject;
        group.Newest.Should().Be(older, "the active mention is shown in the active section, not in history");
        group.OlderCount.Should().Be(0);
    }

    [Fact]
    public async Task ListHistoryShouldCapGroupsToTheNewestOnes()
    {
        // arrange - one chat per notification, one chat more than the cap, oldest first
        var history = Enumerable
            .Range(1, NotificationsUI.MaxHistoryGroups + 1)
            .Select(seq => NewHistory(NotificationKind.Mention, ChatId.Parse(GroupChatId.New().Value), 1, seq))
            .ToArray();
        await using var scope = NewScope([], history);
        var notificationsUI = scope.ServiceProvider.GetRequiredService<NotificationsUI>();

        // act
        var groups = await notificationsUI.ListHistory(ChatListFilter.UnreadMentions.Id);

        // assert
        groups.Should().HaveCount(NotificationsUI.MaxHistoryGroups);
        groups.Select(g => g.Newest.Seq).Should()
            .BeInDescendingOrder("newest first")
            .And.NotContain(1, "the oldest chat is the one the cap drops");
    }

    // Private methods

    // A scoped container around a NotificationsUI whose INotifications.ListActive returns exactly
    // the given set, so the tests go through the public compute methods rather than their internals.
    private AsyncServiceScope NewScope(params Notification[] active)
        => NewScope(active, []);

    private AsyncServiceScope NewScope(Notification[] active, NotificationHistoryItem[] history)
    {
        var notifications = new Mock<INotifications>();
        notifications
            .Setup(x => x.ListActive(It.IsAny<Session>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiArray.New(active));
        notifications
            .Setup(x => x.GetHistoryVersion(It.IsAny<Session>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(history.Length == 0 ? 0 : history.Max(x => x.Seq));
        notifications
            .Setup(x => x.ListHistory(
                It.IsAny<Session>(),
                It.IsAny<NotificationHistoryQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session _, NotificationHistoryQuery q, CancellationToken _) => {
                // The walk order and the limit are the query's, so dropping either from the
                // production query changes what the tests see
                var items = history.Where(x => q.Kinds.IsEmpty || q.Kinds.Contains(x.Kind));
                items = q.IsNewestFirst
                    ? items.OrderByDescending(x => x.Seq)
                    : items.OrderBy(x => x.Seq);
                if (q.Limit > 0)
                    items = items.Take(q.Limit);

                return items.ToApiArray();
            });
        var hostInfo = new HostInfo {
            HostKind = HostKind.MauiApp,
            AppKind = AppKind.Ios,
            Environment = Environments.Development,
            BaseUrl = $"https://{Constants.Hosts.LocalVoxt}",
            IsTested = true,
        };
        var scope = new ServiceCollection()
            .AddTestLogging(Out)
            .AddSingleton(_ => hostInfo)
            .AddSingleton(c => new Features(c))
            .AddSingleton(_ => new UrlMapper(hostInfo))
            .AddSingleton(notifications.Object)
            .AddScoped<UIHub>()
            .AddScoped<AppUIHub>()
            .AddFusion(fusion => {
                fusion.AddBlazor();
                fusion.AddService<NotificationsUI>(ServiceLifetime.Scoped);
            })
            .BuildServiceProvider()
            .CreateAsyncScope();
        // Fusion's scoped SessionResolver starts empty, and the hub throws on the first Session read otherwise
        scope.ServiceProvider.GetRequiredService<ISessionResolver>().Session = Session.New();
        return scope;
    }

    private static ReactionNotification NewReaction(long entryLid, Moment sentAt)
        => ReactionNotification.New(TestUserId, ChatEntryId.New(ChatA, entryLid)) with { SentAt = sentAt };

    private static AttentionNotification NewAttention(ChatId chatId, long entryLid, Moment sentAt)
        => AttentionNotification.New(TestUserId, ChatEntryId.New(chatId, entryLid)) with { SentAt = sentAt };

    private static NotificationHistoryItem NewHistory(NotificationKind kind, ChatId chatId, long entryLid, long seq)
    {
        var entryId = ChatEntryId.New(chatId, entryLid);
        var similarityKey = kind is NotificationKind.Mention or NotificationKind.Attention or NotificationKind.Reaction
            ? entryId.Value
            : chatId.Value;
        return new NotificationHistoryItem(seq, kind) {
            SentAt = Moment.EpochStart + TimeSpan.FromSeconds(seq),
            ChatId = chatId,
            EntryId = entryId,
            Title = "Chat",
            Text = $"#{seq}",
            NotificationId = NotificationId.New(TestUserId, kind, similarityKey),
        };
    }
}
