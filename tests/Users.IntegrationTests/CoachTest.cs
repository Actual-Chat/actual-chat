using ActualChat.Chat;
using ActualChat.Hashing;
using ActualChat.Queues;
using ActualChat.Chat.Module;
using ActualChat.Testing.Host;
using ActualChat.Users.Module;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class CoachTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly Moment T0 = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);

    private ICoachBackend Backend => AppHost.Services.GetRequiredService<ICoachBackend>();
    private IServerKvasBackend Kvas => AppHost.Services.GetRequiredService<IServerKvasBackend>();
    private ICoach Coach => AppHost.Services.GetRequiredService<ICoach>();

    private static CoachEntryAnalysis Entry(
        UserId userId, ChatId chatId, long lid, int words, double seconds, Moment at,
        int fillers = 0, string? word = null)
        => new (ChatEntryId.New(chatId, lid), 1) {
            AuthorId = AuthorId.New(chatId, 1),
            UserId = userId,
            BeginsAt = at,
            Language = Languages.English,
            DurationSeconds = seconds,
            SpeechSeconds = seconds,
            Words = words,
            Sentences = 2,
            DistinctWords = words,
            Fillers = fillers,
            TagState = CoachTagState.Tagged,
            PromptVersion = 1,
            Spans = word is null
                ? ApiArray<SpeechSpan>.Empty
                : Enumerable.Range(0, fillers)
                    .Select(i => new SpeechSpan(
                        SpeechSpanKind.Filler, word, i * 10, word.Length, ApiArray<string>.Empty))
                    .ToApiArray(),
            ContentHash = ChatEntryHashExt.GetContentHashString($"{lid}"),
        };

    private static CoachConversationAnalysis Run(UserId userId, ChatId chatId, long start, Moment endsAt)
        => new (ConversationId.New(chatId, start), AuthorId.New(chatId, 1), 1) {
            UserId = userId,
            ConversationVersion = start,
            EndsAt = endsAt,
            OwnSpeechSeconds = 30,
            TotalSpeechSeconds = 90,
            OwnTurns = 2,
            TotalTurns = 5,
            Participants = 3,
            LongestMonologueSeconds = 20,
            Responses = 1,
            ResponseGapSeconds = 0.9,
            Interruptions = 0,
        };

    private Task<CoachDay> WhenDay(UserId userId, Moment day, Func<CoachDay, bool> ready)
        => TestWait.When(async ct => {
            var days = await Backend.ListDays(userId, new Range<Moment>(day, day + TimeSpan.FromDays(1)), ct);
            days.Should().ContainSingle();
            ready(days[0]).Should().BeTrue();
            return days[0];
        });

    [Fact]
    public async Task EntryEventShouldBuildTheDay()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();

        // act
        var entry = Entry(account.Id, chatId, 1, 40, 20, T0, 2, "you know");
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(entry, false));

        // assert
        var day = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);
        day.Words.Should().Be(40);
        day.TaggedWords.Should().Be(40);
        day.Fillers.Should().Be(2);
        day.FillerCounts["you know"].Should().Be(2);
    }

    [Fact]
    public async Task RedeliveryShouldKeepOneRow()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var e = new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), false);

        // act
        await Queues.Enqueue(e);
        await Queues.Enqueue(e);
        await Queues.WhenProcessing(TimeSpan.FromSeconds(1), default);

        // assert
        var day = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);
        day.Words.Should().Be(40);
    }

    [Fact]
    public async Task ReEmitShouldReplaceTheRow()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), false));
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Words == 40);

        // act
        var edited = Entry(account.Id, chatId, 1, 55, 20, T0) with { Version = 2 };
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(edited, false));

        // assert
        var day = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Words == 55);
        day.Entries.Should().Be(1, "same source id replaces, never duplicates");
    }

    [Fact]
    public async Task RemovalShouldDropTheRowAndRebuildTheDay()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 2, 10, 5, T0), false));
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 2);

        // act
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), true));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 99, 1, 1, T0), true));
        await Queues.WhenProcessing(TimeSpan.FromSeconds(1), default);

        // assert
        var day = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);
        day.Words.Should().Be(10);
    }

    [Fact]
    public async Task RunEventShouldAddTurnTaking()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();

        // act
        await Queues.Enqueue(new CoachConversationAnalyzedEvent(Run(account.Id, chatId, 1, T0)));

        // assert
        var day = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Runs == 1);
        day.OwnSpeechSeconds.Should().Be(30);
        day.FairShareSeconds.Should().Be(30);
    }

    [Fact]
    public async Task RebuildShouldReproduceTheDays()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), false));
        var before = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);

        // act
        await Commander.Call(new CoachBackend_RebuildDays(account.Id));

        // assert
        var after = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);
        after.Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task FillerStepShouldStoreATipForAnOptedInUser()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Kvas.ForUser(account.Id, isOutermost: true).UserCoachSettings()
            .Set(new UserCoachSettings { IsCoachingEnabled = true });
        var now = Clocks.SystemClock.Now;

        // act
        var entry = Entry(account.Id, chatId, 1, 100, 60, now, 10, "like");
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(entry, false));

        // assert
        var tip = await TestWait.When(async ct => {
            var t = await Kvas.ForUser(account.Id).UserCoachTip().Get(ct);
            t.IsPending.Should().BeTrue();
            return t;
        });
        tip.Kind.Should().Be(CoachTipKind.Filler);
        tip.Word.Should().Be("like");
        tip.ChatId.Should().Be(chatId);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task TipsShouldRequireBothToggles(bool coaching, bool liveTips)
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Kvas.ForUser(account.Id, isOutermost: true).UserCoachSettings()
            .Set(new UserCoachSettings { IsCoachingEnabled = coaching, AreLiveTipsEnabled = liveTips });
        var now = Clocks.SystemClock.Now;

        // act
        var entry = Entry(account.Id, chatId, 1, 100, 60, now, 10, "like");
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(entry, false));
        await WhenDay(account.Id, UsageDay.DayOf(now), d => d.Entries == 1);
        await Queues.WhenProcessing(TimeSpan.FromSeconds(1), default);

        // assert
        (await Kvas.ForUser(account.Id).UserCoachTip().Get(default)).IsPending.Should().BeFalse();
    }

    [Fact]
    public async Task ListOccurrencesShouldReturnLatestFirst()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0, 2, "like"), false));
        var later = Entry(account.Id, chatId, 2, 40, 20, T0 + TimeSpan.FromHours(1), 1, "like");
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(later, false));
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 2);

        // act
        var range = new Range<Moment>(T0 - TimeSpan.FromDays(1), T0 + TimeSpan.FromDays(1));
        var occurrences = await Backend.ListOccurrences(account.Id, "like", range, 10, default);

        // assert
        occurrences.Should().HaveCount(3);
        occurrences[0].EntryLid.Should().Be(2, "latest first");
        occurrences.Should().OnlyContain(o => o.ChatId == chatId);
    }

    [Fact]
    public async Task SummaryShouldScoreTheWindowAndDismissTheTip()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Kvas.ForUser(account.Id, isOutermost: true).UserCoachSettings()
            .Set(new UserCoachSettings { IsCoachingEnabled = true });
        var now = Clocks.SystemClock.Now;
        var entry = Entry(account.Id, chatId, 1, 300, 120, now, 10, "like");
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(entry, false));
        await WhenDay(account.Id, UsageDay.DayOf(now), d => d.Entries == 1);

        // act
        var summary = await Coach.GetOwnSummary(tester.Session, CoachWindow.Today, default);
        var tip = await TestWait.When(async ct => {
            var t = await Coach.GetPendingTip(tester.Session, ct);
            t.Should().NotBeNull();
            return t!;
        });
        await tester.Commander.Call(new Coach_DismissTip { Session = tester.Session });

        // assert
        summary.Words.Should().Be(300);
        summary.Score.Should().NotBeNull();
        summary.Metrics.Single(m => m.Kind == CoachMetricKind.Fillers).Chips
            .Should().ContainSingle(c => c.Word == "like" && c.Count == 10);
        tip.Kind.Should().Be(CoachTipKind.Filler);
        await TestWait.When(async ct => (await Coach.GetPendingTip(tester.Session, ct)).Should().BeNull());
    }

    [Fact]
    public async Task GuestShouldGetEmptySummary()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);

        // act
        var summary = await Coach.GetOwnSummary(tester.Session, CoachWindow.Week, default);
        var tip = await Coach.GetPendingTip(tester.Session, default);

        // assert
        summary.Should().Be(CoachSummary.None with { Window = CoachWindow.Week });
        tip.Should().BeNull();
    }

    [Fact]
    public async Task ListOwnOccurrencesShouldStayWithinTheWindow()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var now = Clocks.SystemClock.Now;
        var recent = Entry(account.Id, chatId, 1, 40, 20, now, 1, "like");
        var old = Entry(account.Id, chatId, 2, 40, 20, now - TimeSpan.FromDays(40), 1, "like");
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(recent, false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(old, false));
        await WhenDay(account.Id, UsageDay.DayOf(now), d => d.Entries == 1);
        await WhenDay(account.Id, UsageDay.DayOf(now - TimeSpan.FromDays(40)), d => d.Entries == 1);

        // act
        var month = await Coach.ListOwnOccurrences(tester.Session, "like", CoachWindow.Month, default);
        var all = await Coach.ListOwnOccurrences(tester.Session, "like", CoachWindow.AllTime, default);

        // assert
        month.Should().ContainSingle().Which.EntryLid.Should().Be(1);
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeletingTheAccountShouldRemoveCoachRows()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), false));
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);

        // act
        await tester.Commander.Call(new Accounts_DeleteOwn { Session = tester.Session });

        // assert
        var dbHub = AppHost.Services.GetRequiredService<DbHub<UsersDbContext>>();
        await using var dbContext = await dbHub.CreateDbContext(false);
        (await dbContext.CoachEvents.CountAsync(e => e.UserId == account.Id.Value)).Should().Be(0);
        (await dbContext.CoachDays.CountAsync(d => d.UserId == account.Id.Value)).Should().Be(0);
    }

    private async Task<long> DayRowVersion(UserId userId, Moment day)
    {
        var dbHub = AppHost.Services.GetRequiredService<DbHub<UsersDbContext>>();
        await using var dbContext = await dbHub.CreateDbContext(false);
        var dbDay = day.ToDateTimeClamped();
        return await dbContext.CoachDays
            .Where(d => d.UserId == userId.Value && d.Day == dbDay)
            .Select(d => d.Version)
            .SingleAsync();
    }

    [Fact]
    public async Task RedeliveryShouldNotRewriteTheRow()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var e = new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0, 1, "ну"), false);
        await Queues.Enqueue(e);
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);
        var version = await DayRowVersion(account.Id, UsageDay.DayOf(T0));

        // act
        await Queues.Enqueue(e);
        await Queues.WhenProcessing(TimeSpan.FromSeconds(1), default);

        // assert
        (await DayRowVersion(account.Id, UsageDay.DayOf(T0)))
            .Should().Be(version, "an identical redelivery must not rebuild the day");
    }

    [Fact]
    public async Task StaleEventShouldNotOverwriteANewerRow()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var newer = Entry(account.Id, chatId, 1, 55, 20, T0) with { Version = 2 };
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(newer, false));
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Words == 55);

        // act
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), false));
        await Queues.WhenProcessing(TimeSpan.FromSeconds(1), default);

        // assert
        var day = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);
        day.Words.Should().Be(55, "a late delivery of an older version is ignored");
    }

    [Fact]
    public async Task LateEventAfterRemovalShouldNotResurrectTheRow()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var entry = Entry(account.Id, chatId, 1, 40, 20, T0);
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(entry, false));
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(entry, true));
        await Queues.WhenProcessing(TimeSpan.FromSeconds(1), default);

        // act
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(entry, false));
        await Queues.WhenProcessing(TimeSpan.FromSeconds(1), default);

        // assert
        var range = new Range<Moment>(UsageDay.DayOf(T0), UsageDay.DayOf(T0) + TimeSpan.FromDays(1));
        (await Backend.ListDays(account.Id, range, default)).Should().BeEmpty("a removed message stays removed");
    }

    [Fact]
    public async Task NewEntryShouldShowUpInOccurrences()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var range = new Range<Moment>(T0 - TimeSpan.FromDays(1), T0 + TimeSpan.FromDays(1));
        (await Backend.ListOccurrences(account.Id, "like", range, 10, default)).Should().BeEmpty();

        // act
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0, 1, "like"), false));

        // assert
        await TestWait.When(async ct => (await Backend.ListOccurrences(account.Id, "like", range, 10, ct))
            .Should().ContainSingle("a new entry must invalidate the occurrences"));
    }

    [Fact]
    public async Task OccurrencesShouldLookPastTheLatestHundredEntries()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0, 1, "like"), false));
        for (var i = 2; i <= 121; i++)
            await Queues.Enqueue(new CoachEntryAnalyzedEvent(
                Entry(account.Id, chatId, i, 40, 20, T0 + TimeSpan.FromMinutes(i)), false));
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 121);

        // act
        var range = new Range<Moment>(T0 - TimeSpan.FromDays(1), T0 + TimeSpan.FromDays(1));
        var occurrences = await Backend.ListOccurrences(account.Id, "like", range, 20, default);

        // assert
        occurrences.Should().ContainSingle("the chip count promises every occurrence in the window");
    }

    [Fact]
    public async Task ConcurrentEventsShouldAllLandInTheDay()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();

        // act
        await Task.WhenAll(Enumerable.Range(1, 8).Select(i =>
            Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, i, 10, 5, T0), false))));

        // assert
        await Queues.WhenProcessing(TimeSpan.FromSeconds(1), default);
        var day = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 8);
        day.Words.Should().Be(80);
    }

    [Fact]
    public async Task IsEnabledShouldFollowTheRolloutRule()
    {
        // arrange
        var coach = $"{nameof(ChatSettings)}:{nameof(ChatSettings.Coach)}:{nameof(CoachSettings.IsEnabled)}";
        var rollout = $"{nameof(UsersSettings)}:{nameof(UsersSettings.Coach)}:{nameof(CoachScoringSettings.Rollout)}";
        await using var focusHost = await NewAppHost("coach-rollout-focus", o => o with {
            ConfigureHost = (_, cfg) => cfg.AddInMemoryCollection((coach, "true")),
        });
        await using var everyoneHost = await NewAppHost("coach-rollout-everyone", o => o with {
            ConfigureHost = (_, cfg) => cfg.AddInMemoryCollection(
                (coach, "true"),
                (rollout, nameof(CoachRollout.Everyone))),
        });
        await using var guest = focusHost.NewWebClientTester(Out);
        await using var bob = focusHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();
        await using var admin = focusHost.NewWebClientTester(Out);
        await admin.SignInAsUniqueBobAdmin();
        await using var anyone = everyoneHost.NewWebClientTester(Out);
        await anyone.SignInAsUniqueBob();
        var focusCoach = focusHost.Services.GetRequiredService<ICoach>();
        var everyoneCoach = everyoneHost.Services.GetRequiredService<ICoach>();

        // assert
        (await focusCoach.IsEnabled(guest.Session, default)).Should().BeFalse();
        (await focusCoach.IsEnabled(bob.Session, default)).Should().BeFalse("not an admin, not in the focus group");
        (await focusCoach.IsEnabled(admin.Session, default)).Should().BeTrue();
        (await everyoneCoach.IsEnabled(anyone.Session, default)).Should().BeTrue();
        (await Coach.IsEnabled(bob.Session, default)).Should().BeFalse("the shared host has the master switch off");
    }
}
