using ActualChat.Notifications.Flows;
using ActualChat.UI.Blazor.Resources;

namespace ActualChat.Notifications.IntegrationTests;

public class NotificationAggregationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly UserId TestUserId = UserId.New();
    private static readonly LanguageStringLocalizer English = LanguageStringLocalizer.Get(Languages.English);

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

        var merged = info.Items.Single().Should().BeOfType<MessageNotification>().Subject;
        merged.StartEntryLid.Should().Be(100);
        merged.EntryLid.Should().Be(101);
        merged.StartEntryId.Should().Be(ChatEntryId.New(TestChatId, 100));
        merged.UnreadCount.Should().Be(2);
        merged.AuthorIds.Should().BeEquivalentTo(new[] { author1, author2 });
        merged.RecentMessages.Select(m => m.Text)
            .Should().Equal("Hey team, here is the long first message", "second");
        merged.LeadText.Should().Be("second");
        merged.LeadCount.Should().Be(1);
    }

    [Fact]
    public void MergeEvictsOldestBeyondCapacity()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var info = new UserNotificationInfo(TestUserId);
        for (var lid = 100; lid < 107; lid++)
            info = info.WithNotification(NewMessage(lid, author1, $"m{lid}"));

        var merged = (MessageNotification)info.Items.Single();
        merged.UnreadCount.Should().Be(7);
        merged.RecentMessages.Should().HaveCount(Constants.Notification.MaxRecentMessages);
        merged.RecentMessages.Select(m => m.Text).Should().Equal("m102", "m103", "m104", "m105", "m106");
        merged.StartEntryLid.Should().Be(100);
        merged.LeadText.Should().Be("m106");
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

        var merged = (MessageNotification)info.Items.Single();
        merged.UnreadCount.Should().Be(2);
        merged.RecentMessages.Select(m => m.Text).Should().Equal("Hi", "are you there?");
        merged.LeadText.Should().Be("are you there?");
        merged.LeadCount.Should().Be(1);
        merged.StartEntryLid.Should().Be(100);
        merged.EntryLid.Should().Be(101);
    }

    [Fact]
    public void MergeKeepsNewestSentAtAndTitleOnOutOfOrderMerge()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var t0 = Moment.Now;
        var t1 = t0 + TimeSpan.FromSeconds(30);
        var existing = NewMessage(101, author1, "second") with { SentAt = t1, Title = "Bob @ Chat" };
        var late = NewMessage(100, author1, "first") with { SentAt = t0, Title = "Alice @ Chat" };

        // The delayed earlier message extends the window, but must regress neither the timestamp
        // (a regressed SentAt would fake a lull) nor the headline (title tracks the newest message).
        var merged = (MessageNotification)late.MergeWith(existing);
        merged.SentAt.Should().Be(t1);
        merged.Title.Should().Be("Bob @ Chat");
        merged.StartEntryLid.Should().Be(100);
        merged.RecentMessages.Select(m => m.Text).Should().Equal("first", "second");
        merged.LeadText.Should().Be("second");
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
    public void AggregatedTextIsNewestFirstWithAuthorPrefixesInGroupChat()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var author2 = AuthorId.New(TestChatId, 2);
        var first = NewMessage(100, author1, "who takes the release?", "Alice");
        var second = NewMessage(101, author2, "I fixed the flaky test", "Bob");

        var merged = (MessageNotification)second.MergeWith(first);

        NotificationHelper.ComposeAggregatedText(merged, English)
            .Should().Be("Bob: I fixed the flaky test\nAlice: who takes the release?");
    }

    [Fact]
    public void AggregatedTextSkipsAuthorNamesInPeerChat()
    {
        var peerChatId = PeerChatId.New(UserId.New(), UserId.New());
        var author = AuthorId.New(peerChatId, 1);
        var first = MessageNotification.New(TestUserId, peerChatId, 100, author) with {
            StartEntryLid = 100,
            UnreadCount = 1,
            RecentMessages = new[] { NotificationMessage.New(author, "Alice", "first", 100, Moment.Now) }.ToApiArray(),
        };
        var second = MessageNotification.New(TestUserId, peerChatId, 101, author) with {
            StartEntryLid = 101,
            UnreadCount = 1,
            RecentMessages = new[] { NotificationMessage.New(author, "Alice", "second", 101, Moment.Now) }.ToApiArray(),
        };

        var merged = (MessageNotification)second.MergeWith(first);

        NotificationHelper.ComposeAggregatedText(merged, English).Should().Be("second\nfirst");
    }

    [Fact]
    public void AggregatedTextCountsOnlyMessagesBeyondWindow()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var info = new UserNotificationInfo(TestUserId);
        for (var lid = 100; lid < 106; lid++)
            info = info.WithNotification(NewMessage(lid, author1, $"m{lid}", "Alice"));
        var merged = (MessageNotification)info.Items.Single();

        var text = NotificationHelper.ComposeAggregatedText(merged, English);

        text.Should().StartWith("Alice: m105");
        text.Should().EndWith("+1 earlier message");
        merged.UnreadCount.Should().Be(6);
    }

    [Fact]
    public void AggregatedTextFallsBackForLegacyNotification()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var legacy = MessageNotification.New(TestUserId, TestChatId, 100, author1) with {
            Text = "composed old body",
            LeadText = "old lead",
        };

        NotificationHelper.ComposeAggregatedText(legacy, English).Should().Be("old lead");
    }

    [Fact]
    public void ReAnchorAtDropsReadMessages()
    {
        var author = AuthorId.New(TestChatId, 1);
        var info = new UserNotificationInfo(TestUserId);
        for (var lid = 100; lid < 105; lid++)
            info = info.WithNotification(NewMessage(lid, author, $"m{lid}", "Alice"));
        var merged = (MessageNotification)info.Items.Single();

        var reAnchored = merged.ReAnchorAt(103);

        reAnchored.StartEntryLid.Should().Be(103);
        reAnchored.UnreadCount.Should().Be(2);
        reAnchored.RecentMessages.Select(m => m.Text).Should().Equal("m103", "m104");
        reAnchored.LeadText.Should().Be("m104");
    }

    [Fact]
    public void ReAnchorAtEmptiesWindowWhenAllShownAreRead()
    {
        var author = AuthorId.New(TestChatId, 1);
        var n = NewMessage(100, author, "only", "Alice");

        var reAnchored = n.ReAnchorAt(101);

        reAnchored.RecentMessages.Should().BeEmpty();
        reAnchored.LeadText.Should().Be("");
        reAnchored.UnreadCount.Should().Be(1);
    }

    [Fact]
    public void MergeUpgradesLegacyNotification()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        // A pre-RecentMessages blob deserializes with an empty list; its text lives in LeadText/Text.
        var legacy = MessageNotification.New(TestUserId, TestChatId, 100, author1) with { Text = "old text" };
        var incoming = NewMessage(101, author1, "new text");

        var merged = (MessageNotification)incoming.MergeWith(legacy);
        merged.UnreadCount.Should().Be(2);
        merged.StartEntryLid.Should().Be(100);
        merged.RecentMessages.Select(m => m.Text).Should().Equal("old text", "new text");
        merged.RecentMessages[0].AuthorName.Should().Be("");
        merged.LeadText.Should().Be("new text");
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
            RecentMessages = new[] {
                NotificationMessage.New(author1, "Alice", "Lead", 100, Moment.Now),
                NotificationMessage.New(author2, "Bob", "Body", 101, Moment.Now),
            }.ToApiArray(),
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
        result.RecentMessages.Should().BeEquivalentTo(n.RecentMessages);
        // ApiArray equality is by reference, so normalize them before the whole-record value compare.
        result.Should().Be(n with { AuthorIds = result.AuthorIds, RecentMessages = result.RecentMessages });
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

    [Fact]
    public void SpokenMessageBeepsOncePerSpeakerRun()
    {
        // arrange
        var t0 = Moment.Now;
        var author = AuthorId.New(TestChatId, 1);
        var group = "a:" + author.Value;
        var first = NewSpoken(100, author, "hi", group);

        // act & assert
        NotificationBeepPolicy.ShouldBeep(first, t0).Should().BeTrue();

        var beeped = first with { LastBeepGroup = group, LastBeepAt = t0, BeepCount = 1 };
        NotificationBeepPolicy.ShouldBeep(beeped, t0 + TimeSpan.FromMinutes(9)).Should().BeFalse();
        NotificationBeepPolicy.ShouldBeep(beeped, t0 + Constants.Notification.VoiceReAlertInterval)
            .Should().BeTrue();
    }

    [Fact]
    public void SpokenMessageBeepsWhenTheSpeakerChanges()
    {
        // arrange
        var t0 = Moment.Now;
        var author1 = AuthorId.New(TestChatId, 1);
        var author2 = AuthorId.New(TestChatId, 2);
        var handover = NewSpoken(101, author2, "my turn", "a:" + author2.Value) with {
            LastBeepGroup = "a:" + author1.Value,
            LastBeepAt = t0,
            BeepCount = 3,
        };

        // act & assert: the interval only throttles a run by one speaker
        NotificationBeepPolicy.ShouldBeep(handover, t0 + TimeSpan.FromSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void MonologueBeepsOnlyOncePerInterval()
    {
        // arrange
        var t0 = Moment.Now;
        var author = AuthorId.New(TestChatId, 1);
        var group = "a:" + author.Value;
        var monologue = NewSpoken(101, author, "still talking", group) with {
            LastBeepGroup = group,
            LastBeepAt = t0,
        };

        // act & assert
        NotificationBeepPolicy.ShouldBeep(monologue, t0 + TimeSpan.FromMinutes(5)).Should().BeFalse();
        NotificationBeepPolicy.ShouldBeep(monologue, t0 + Constants.Notification.VoiceReAlertInterval)
            .Should().BeTrue();
    }

    [Fact]
    public void TypedMessageKeepsTheBeepBackoff()
    {
        // arrange
        var t0 = Moment.Now;
        var typed = NewMessage(101, AuthorId.New(TestChatId, 1), "typed") with {
            BeepCount = 1,
            LastBeepAt = t0,
        };

        // act & assert
        typed.BeepGroup.Should().BeEmpty();
        NotificationBeepPolicy.ShouldBeep(typed, t0 + TimeSpan.FromSeconds(5)).Should().BeFalse();
        NotificationBeepPolicy.ShouldBeep(typed, t0 + TimeSpan.FromSeconds(10)).Should().BeTrue();
    }

    [Fact]
    public void PreBeepGroupBlobsBehaveAsTyped()
    {
        // arrange: a blob written before keys 19/20 deserializes them as null, not ""
        var t0 = Moment.Now;
        var legacy = NewMessage(101, AuthorId.New(TestChatId, 1), "old") with {
            BeepGroup = null!,
            LastBeepGroup = null!,
            BeepCount = 1,
            LastBeepAt = t0,
        };

        // act & assert
        NotificationBeepPolicy.ShouldBeep(legacy, t0 + TimeSpan.FromSeconds(5)).Should().BeFalse();
        NotificationBeepPolicy.ShouldBeep(legacy, t0 + TimeSpan.FromSeconds(10)).Should().BeTrue();
        var merged = (MessageNotification)NewMessage(102, AuthorId.New(TestChatId, 1), "next").MergeWith(legacy);
        merged.BeepGroup.Should().BeEmpty();
    }

    [Fact]
    public void MergeTracksTheNewestMessageBeepGroup()
    {
        // arrange
        var t0 = Moment.Now;
        var author1 = AuthorId.New(TestChatId, 1);
        var author2 = AuthorId.New(TestChatId, 2);
        var group1 = "a:" + author1.Value;
        var group2 = "a:" + author2.Value;
        var existing = NewSpoken(100, author1, "first", group1) with {
            SentAt = t0,
            LastBeepGroup = group1,
            LastBeepAt = t0,
        };

        // act & assert: the newer message decides the group, and a handover forgets the
        // last-beeped speaker so the new one is still heard
        var newer = NewSpoken(101, author2, "second", group2) with { SentAt = t0 + TimeSpan.FromMinutes(1) };
        var merged = (MessageNotification)newer.MergeWith(existing);
        merged.BeepGroup.Should().Be(group2);
        merged.LastBeepGroup.Should().BeEmpty();
        NotificationBeepPolicy.ShouldBeep(merged, merged.SentAt).Should().BeTrue();

        // an out-of-order earlier message leaves the group alone
        var older = NewSpoken(99, author2, "earlier", group2) with { SentAt = t0 - TimeSpan.FromMinutes(1) };
        ((MessageNotification)older.MergeWith(existing)).BeepGroup.Should().Be(group1);
    }

    [Fact]
    public void MergeResetsTheBeepGroupAfterAVoiceLull()
    {
        // arrange
        var t0 = Moment.Now;
        var author = AuthorId.New(TestChatId, 1);
        var group = "a:" + author.Value;
        var existing = NewSpoken(100, author, "first", group) with {
            SentAt = t0,
            BeepCount = 3,
            LastBeepAt = t0,
            LastBeepGroup = group,
        };

        // act & assert: BeepResetPeriod is not enough for a spoken run - an ordinary pause
        // mid-monologue must not re-arm the beep
        var pause = NewSpoken(101, author, "second", group) with {
            SentAt = t0 + Constants.Notification.BeepResetPeriod,
        };
        var afterPause = (MessageNotification)pause.MergeWith(existing);
        afterPause.LastBeepGroup.Should().Be(group);
        NotificationBeepPolicy.ShouldBeep(afterPause, afterPause.SentAt).Should().BeFalse();

        var lull = NewSpoken(101, author, "second", group) with {
            SentAt = t0 + Constants.Notification.VoiceReAlertInterval,
        };
        var afterLull = (MessageNotification)lull.MergeWith(existing);
        afterLull.LastBeepGroup.Should().BeEmpty();
        NotificationBeepPolicy.ShouldBeep(afterLull, afterLull.SentAt).Should().BeTrue();
    }

    [Fact]
    public void MergeKeepsAHandoverInsideOneBatchAudible()
    {
        // arrange: A already alerted, then B and A both speak before the soft buffer drains
        var t0 = Moment.Now;
        var authorA = AuthorId.New(TestChatId, 1);
        var authorB = AuthorId.New(TestChatId, 2);
        var groupA = "a:" + authorA.Value;
        var groupB = "a:" + authorB.Value;
        var items = NewSpoken(100, authorA, "one", groupA) with {
            SentAt = t0,
            BeepCount = 1,
            LastBeepAt = t0,
            LastBeepGroup = groupA,
        };

        // act
        var fromB = NewSpoken(101, authorB, "two", groupB) with { SentAt = t0 + TimeSpan.FromSeconds(2) };
        var merged = (ChatEntryRelatedNotification)fromB.MergeWith(items);
        var fromA = NewSpoken(102, authorA, "three", groupA) with { SentAt = t0 + TimeSpan.FromSeconds(4) };
        merged = (ChatEntryRelatedNotification)fromA.MergeWith(merged);

        // assert: only A's group survives the merge, but B's turn still has to be heard
        merged.BeepGroup.Should().Be(groupA);
        NotificationBeepPolicy.ShouldBeep(merged, t0 + TimeSpan.FromSeconds(4)).Should().BeTrue();
    }

    [Fact]
    public void TypedAlertKeepsTheRememberedSpeaker()
    {
        // arrange
        var t0 = Moment.Now;
        var author = AuthorId.New(TestChatId, 1);
        var group = "a:" + author.Value;

        // act: the utterance alerts and is remembered
        var afterVoice = NotificationBeepPolicy.MarkBeeped(NewSpoken(100, author, "one", group), t0);
        afterVoice.LastBeepGroup.Should().Be(group);

        // a typed message alerting mid-run must not erase it
        var typed = afterVoice with { BeepGroup = "" };
        var afterTyped = NotificationBeepPolicy.MarkBeeped(typed, t0 + TimeSpan.FromSeconds(30));

        // assert: the next utterance from the same speaker stays silent
        afterTyped.LastBeepGroup.Should().Be(group);
        var nextUtterance = afterTyped with { BeepGroup = group };
        NotificationBeepPolicy.ShouldBeep(nextUtterance, t0 + TimeSpan.FromMinutes(1)).Should().BeFalse();
    }

    private static MessageNotification NewSpoken(
        long entryLid, AuthorId authorId, string text, string beepGroup)
        => NewMessage(entryLid, authorId, text) with { BeepGroup = beepGroup };

    private static MessageNotification NewMessage(long entryLid, AuthorId authorId, string text, string authorName = "")
        => MessageNotification.New(TestUserId, TestChatId, entryLid, authorId) with {
            Text = text,
            StartEntryLid = entryLid,
            UnreadCount = 1,
            AuthorIds = new[] { authorId }.ToApiArray(),
            RecentMessages = new[] {
                NotificationMessage.New(
                    authorId, authorName, text, entryLid, Moment.EpochStart + TimeSpan.FromSeconds(entryLid)),
            }.ToApiArray(),
            LeadText = text,
            LeadCount = 1,
        };
}
