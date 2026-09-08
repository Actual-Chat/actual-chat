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

    // Private methods

    // A scoped container around a NotificationsUI whose INotifications.ListActive returns exactly
    // the given set, so the tests go through the public compute methods rather than their internals.
    private AsyncServiceScope NewScope(params Notification[] active)
    {
        var notifications = new Mock<INotifications>();
        notifications
            .Setup(x => x.ListActive(It.IsAny<Session>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiArray.New(active));
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
}
