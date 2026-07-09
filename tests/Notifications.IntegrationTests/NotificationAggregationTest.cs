using ActualChat.Notifications.Flows;

namespace ActualChat.Notifications.IntegrationTests;

public class NotificationAggregationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly UserId TestUserId = UserId.New();

    [Fact]
    public void ShouldBeepFirstAlertAlways()
    {
        var now = Moment.Now;
        NotificationBeepPolicy.ShouldBeep(NotificationKind.Message, 0, default, now).Should().BeTrue();
        NotificationBeepPolicy.ShouldBeep(NotificationKind.Mention, 0, default, now).Should().BeTrue();
    }

    [Fact]
    public void ShouldBeepBacksOff()
    {
        var t0 = Moment.Now;

        // second alert needs a ~10s gap
        NotificationBeepPolicy.ShouldBeep(NotificationKind.Message, 1, t0, t0 + TimeSpan.FromSeconds(5)).Should().BeFalse();
        NotificationBeepPolicy.ShouldBeep(NotificationKind.Message, 1, t0, t0 + TimeSpan.FromSeconds(10)).Should().BeTrue();

        // deep into the burst the interval clamps to the last (30 min) entry
        NotificationBeepPolicy.ShouldBeep(NotificationKind.Message, 9, t0, t0 + TimeSpan.FromMinutes(10)).Should().BeFalse();
        NotificationBeepPolicy.ShouldBeep(NotificationKind.Message, 9, t0, t0 + TimeSpan.FromMinutes(30)).Should().BeTrue();
    }

    [Fact]
    public void MergeResetsBeepBackoffAfterLull()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var t0 = Moment.Now;
        var existing = NewMessage(100, author1, "first") with { SentAt = t0, BeepCount = 4, LastBeepAt = t0 };

        // A follow-up within the lull window keeps the back-off...
        var soon = NewMessage(101, author1, "second") with { SentAt = t0 + TimeSpan.FromMinutes(1) };
        ((MessageNotification)soon.MergeWith(existing)).BeepCount.Should().Be(4);

        // ...but after a lull >= BeepResetPeriod the back-off resets so the next alert fires immediately.
        var afterLull = NewMessage(101, author1, "second") with { SentAt = t0 + Constants.Notification.BeepResetPeriod };
        var merged = (MessageNotification)afterLull.MergeWith(existing);
        merged.BeepCount.Should().Be(0);
        NotificationBeepPolicy.ShouldBeep(NotificationKind.Message, merged.BeepCount, merged.LastBeepAt, afterLull.SentAt)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldBeepMentionUsesFixedInterval()
    {
        var t0 = Moment.Now;
        NotificationBeepPolicy.ShouldBeep(NotificationKind.Mention, 1, t0, t0 + TimeSpan.FromMinutes(5)).Should().BeFalse();
        NotificationBeepPolicy.ShouldBeep(NotificationKind.Mention, 1, t0, t0 + TimeSpan.FromMinutes(10)).Should().BeTrue();
    }

    [Fact]
    public void MentionIsDueAfterInterval()
    {
        var entryId = ChatEntryId.New(TestChatId, 7);
        var authorId = AuthorId.New(TestChatId, 1);
        var t0 = Moment.Now;
        var mention = MentionNotification.New(TestUserId, entryId, authorId) with { SentAt = t0 };

        MentionReminderFlow.IsDue(mention, t0 + TimeSpan.FromMinutes(5)).Should().BeFalse();
        MentionReminderFlow.IsDue(mention, t0 + Constants.Notification.MentionReAlertInterval).Should().BeTrue();
    }

    [Fact]
    public void MentionReAlertsAreCapped()
    {
        var entryId = ChatEntryId.New(TestChatId, 7);
        var t0 = Moment.Now;
        var mention = MentionNotification.New(TestUserId, entryId, AuthorId.New(TestChatId, 1)) with { SentAt = t0 };
        var due = t0 + Constants.Notification.MentionReAlertInterval;

        for (var count = 0; count < Constants.Notification.MaxMentionReAlerts; count++)
            MentionReminderFlow.ShouldReAlert(mention, due, count).Should().BeTrue();
        MentionReminderFlow.ShouldReAlert(mention, due, Constants.Notification.MaxMentionReAlerts).Should().BeFalse();
    }

    [Fact]
    public void MergeAccumulatesUnreadAndAuthors()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var author2 = AuthorId.New(TestChatId, 2);
        var first = NewMessage(100, author1, "Hey team, here is the long first message");
        var second = NewMessage(101, author2, "second");

        var info = new UserNotificationInfo(TestUserId)
            .WithNotification(first)
            .WithNotification(second);

        var merged = info.Displayed.Single().Should().BeOfType<MessageNotification>().Subject;
        merged.StartEntryLid.Should().Be(100);
        merged.EntryLid.Should().Be(101);
        merged.StartEntryId.Should().Be(ChatEntryId.New(TestChatId, 100));
        merged.UnreadCount.Should().Be(2);
        merged.AuthorIds.Should().BeEquivalentTo(new[] { author1, author2 });
        merged.LeadText.Should().Be("Hey team, here is the long first message");
    }

    [Fact]
    public void MergeRollsInShortFirstMessage()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var first = NewMessage(100, author1, "Hi");
        var second = NewMessage(101, author1, "are you there?");

        var info = new UserNotificationInfo(TestUserId)
            .WithNotification(first)
            .WithNotification(second);

        var merged = (MessageNotification)info.Displayed.Single();
        merged.LeadText.Should().Be("Hi\nare you there?");
        merged.LeadCount.Should().Be(2);
        merged.AuthorIds.Should().ContainSingle().Which.Should().Be(author1);
        merged.UnreadCount.Should().Be(2);
    }

    [Fact]
    public void MergeIsIdempotentOnRedelivery()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var first = NewMessage(100, author1, "Hi");
        var second = NewMessage(101, author1, "are you there?");

        // A redelivered duplicate of an already-merged event must not change anything.
        var info = new UserNotificationInfo(TestUserId)
            .WithNotification(first)
            .WithNotification(second)
            .WithNotification(second)
            .WithNotification(first);

        var merged = (MessageNotification)info.Displayed.Single();
        merged.UnreadCount.Should().Be(2);
        merged.LeadText.Should().Be("Hi\nare you there?");
        merged.LeadCount.Should().Be(2);
        merged.StartEntryLid.Should().Be(100);
        merged.EntryLid.Should().Be(101);
    }

    [Fact]
    public void MergeKeepsNewestSentAtOnOutOfOrderMerge()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var t0 = Moment.Now;
        var t1 = t0 + TimeSpan.FromSeconds(30);
        var existing = NewMessage(101, author1, "second") with { SentAt = t1 };
        var late = NewMessage(100, author1, "first") with { SentAt = t0 };

        // The delayed earlier message becomes the lead, but must not regress the timestamp
        // (a regressed SentAt would fake a lull and reset the beep back-off mid-burst).
        var merged = (MessageNotification)late.MergeWith(existing);
        merged.SentAt.Should().Be(t1);
        merged.StartEntryLid.Should().Be(100);
        merged.LeadText.Should().Be("first");
        merged.LeadCount.Should().Be(1);
        merged.UnreadCount.Should().Be(2);
    }

    [Fact]
    public void MergeOfRedeliveredIndividualNotificationIsReferenceIdempotent()
    {
        // Individually-seen kinds (mention/reaction/attention/call) don't coalesce; a NATS
        // redelivery of the same event must be a no-op so the reconcile skips the duplicate push.
        var entryId = ChatEntryId.New(TestChatId, 7);
        var authorId = AuthorId.New(TestChatId, 1);
        var t0 = Moment.Now;
        var existing = MentionNotification.New(TestUserId, entryId, authorId) with { SentAt = t0 };
        var redelivered = MentionNotification.New(TestUserId, entryId, authorId) with { SentAt = t0 };

        redelivered.MergeWith(existing).Should().BeSameAs(existing);
    }

    [Fact]
    public void MergeOfOutOfOrderOlderIndividualNotificationKeepsExisting()
    {
        var conversationId = ConversationId.New(TestChatId, 5);
        var caller = AuthorId.New(TestChatId, 1);
        var t0 = Moment.Now;
        var existing = CallNotification.New(TestUserId, conversationId, caller, hasVideo: false) with {
            SentAt = t0 + TimeSpan.FromSeconds(10),
        };
        var late = CallNotification.New(TestUserId, conversationId, caller, hasVideo: false) with { SentAt = t0 };

        late.MergeWith(existing).Should().BeSameAs(existing);
    }

    [Fact]
    public void MergeOfNewerIndividualNotificationUpdates()
    {
        var entryId = ChatEntryId.New(TestChatId, 7);
        var authorId = AuthorId.New(TestChatId, 1);
        var t0 = Moment.Now;
        var existing = MentionNotification.New(TestUserId, entryId, authorId) with { SentAt = t0, Text = "old" };
        var newer = MentionNotification.New(TestUserId, entryId, authorId) with {
            SentAt = t0 + TimeSpan.FromSeconds(1),
            Text = "new",
        };

        var merged = newer.MergeWith(existing);
        merged.Should().NotBeSameAs(existing);
        merged.Text.Should().Be("new");
    }

    [Fact]
    public void AggregatedTextCountsOnlyMessagesBeyondLead()
    {
        NotificationHelper.GetAggregatedText("Hi", ["Alice"], 0).Should().Be("Hi");
        NotificationHelper.GetAggregatedText("Hi", ["Alice"], 1).Should().Be("Hi\nAlice · +1 more message");
        NotificationHelper.GetAggregatedText("Hi", [], 2).Should().Be("Hi\n+2 more messages");
    }

    [Fact]
    public void MergeUpgradesLegacyNotification()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        // A pre-coalescing blob deserializes with UnreadCount=0 and no lead/anchor state.
        var legacy = MessageNotification.New(TestUserId, TestChatId, 100, author1) with { Text = "old text" };
        var incoming = NewMessage(101, author1, "new text");

        var merged = (MessageNotification)incoming.MergeWith(legacy);
        merged.UnreadCount.Should().Be(2);
        merged.StartEntryLid.Should().Be(100);
        merged.LeadText.Should().Be("old text");
        merged.LeadCount.Should().Be(1);
    }

    [Fact]
    public void MergePreservesBeepStateAndStartAnchor()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var beepAt = Moment.Now;
        var existing = NewMessage(100, author1, "first") with { BeepCount = 2, LastBeepAt = beepAt };
        var incoming = NewMessage(101, author1, "second");

        var merged = (MessageNotification)incoming.MergeWith(existing);
        merged.BeepCount.Should().Be(2);
        merged.LastBeepAt.Should().Be(beepAt);
        merged.StartEntryLid.Should().Be(100);
    }

    [Fact]
    public void AggregatedNotificationRoundtrips()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var author2 = AuthorId.New(TestChatId, 2);
        var n = MessageNotification.New(TestUserId, TestChatId, 101, author1) with {
            Version = 1,
            Title = "Title",
            Text = "Body",
            StartEntryLid = 100,
            UnreadCount = 5,
            AuthorIds = new[] { author1, author2 }.ToApiArray(),
            LeadText = "Lead",
            LeadCount = 2,
            BeepCount = 3,
            LastBeepAt = Moment.Now,
        };

        var result = n.PassThroughMessagePackByteSerializer(Out);
        result.AuthorIds.Should().BeEquivalentTo(n.AuthorIds);
        result.StartEntryLid.Should().Be(100);
        result.UnreadCount.Should().Be(5);
        result.LeadText.Should().Be("Lead");
        result.LeadCount.Should().Be(2);
        result.BeepCount.Should().Be(3);
        result.LastBeepAt.Should().Be(n.LastBeepAt);
        // ApiArray equality is by reference, so normalize it before the whole-record value compare.
        result.Should().Be(n with { AuthorIds = result.AuthorIds });
    }

    [Fact]
    public void NotificationWithActionsRoundtrips()
    {
        var n = MessageNotification.New(TestUserId, TestChatId, 5) with {
            Version = 1,
            Title = "Title",
            Text = "Body",
            Actions = new[] {
                new NotificationAction(NotificationActionKind.Open, "Open"),
                new NotificationAction(NotificationActionKind.Dismiss, "Dismiss", "/chat/x"),
            }.ToApiArray(),
        };

        var result = n.PassThroughMessagePackByteSerializer(Out);
        result.Actions.Should().HaveCount(2);
        result.Actions[0].Kind.Should().Be(NotificationActionKind.Open);
        result.Actions[0].Title.Should().Be("Open");
        result.Actions[1].Kind.Should().Be(NotificationActionKind.Dismiss);
        result.Actions[1].Target.Should().Be("/chat/x");
    }

    private static MessageNotification NewMessage(long entryLid, AuthorId authorId, string text)
        => MessageNotification.New(TestUserId, TestChatId, entryLid, authorId) with {
            Text = text,
            StartEntryLid = entryLid,
            UnreadCount = 1,
            AuthorIds = new[] { authorId }.ToApiArray(),
            LeadText = text,
            LeadCount = 1,
        };
}
