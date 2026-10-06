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
        int fillers = 0, string? word = null, Language? language = null)
        => new (ChatEntryId.New(chatId, lid), 1) {
            AuthorId = AuthorId.New(chatId, 1),
            UserId = userId,
            BeginsAt = at,
            Language = language ?? Languages.English,
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
            var days = await Backend.ListDays(userId, new Range<Moment>(day, day + TimeSpan.FromDays(1)), null, ct);
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
    public async Task WordUsesInTheWindowShouldStoreATipForAnOptedInUser()
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

    [Fact]
    public async Task WordUsesAcrossRecentEntriesShouldAddUpToATip()
    {
        // arrange: two uses 15 minutes ago and two now, none of the entries reaching three alone
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Kvas.ForUser(account.Id, isOutermost: true).UserCoachSettings()
            .Set(new UserCoachSettings { IsCoachingEnabled = true });
        var now = Clocks.SystemClock.Now;
        var earlier = Entry(account.Id, chatId, 1, 20, 10, now - TimeSpan.FromMinutes(15), 2, "like");
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(earlier, false));
        await WhenDay(account.Id, UsageDay.DayOf(now), d => d.Entries == 1);
        await Queues.WhenProcessing(TimeSpan.FromSeconds(1), default);
        (await Kvas.ForUser(account.Id).UserCoachTip().Get(default)).IsPending
            .Should().BeFalse("two uses are below the count");

        // act
        var current = Entry(account.Id, chatId, 2, 20, 10, now, 2, "like");
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(current, false));

        // assert
        var tip = await TestWait.When(async ct => {
            var t = await Kvas.ForUser(account.Id).UserCoachTip().Get(ct);
            t.IsPending.Should().BeTrue();
            return t;
        });
        tip.Word.Should().Be("like");
        tip.Count.Should().Be(4, "the window holds both entries");
        tip.EntryLid.Should().Be(2);
        tip.WordTipAt.Should().ContainKey("like");
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
        var summary = await Coach.GetOwnSummary(tester.Session, CoachWindow.Today, null, default);
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
        var summary = await Coach.GetOwnSummary(tester.Session, CoachWindow.Week, null, default);
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
        (await Backend.ListDays(account.Id, range, null, default)).Should().BeEmpty("a removed message stays removed");
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

    [Fact]
    public async Task EntriesInTwoLanguagesShouldBuildOneDayRowPerLanguage()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var day = UsageDay.DayOf(T0);

        // act
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 100, 60, T0), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 2, 40, 30, T0 + TimeSpan.FromMinutes(1), language: Languages.Russian), false));
        await Queues.Enqueue(new CoachConversationAnalyzedEvent(
            Run(account.Id, chatId, 1, T0 + TimeSpan.FromMinutes(2))));

        // assert
        var range = new Range<Moment>(day, day + TimeSpan.FromDays(1));
        var rows = await TestWait.When(async ct => {
            var all = await Backend.ListDays(account.Id, range, null, ct);
            all.Should().HaveCount(3, "one row per language and a neutral row for the run");
            all.Should().OnlyContain(d => d.Runs == 1);
            return all;
        });
        rows.Single(d => d.Language == "ru").Words.Should().Be(40);
        var english = await Backend.ListDays(account.Id, range, "en-US", default);
        english.Sum(d => d.Words).Should().Be(100);
        english.Should().Contain(d => d.Language == "", "the neutral row is always included");
        var summary = await Coach.GetOwnSummary(tester.Session, CoachWindow.AllTime, null, default);
        summary.Words.Should().Be(140);
    }

    [Fact]
    public async Task ListOwnConversationsShouldGroupEntriesByChatAndGap()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatA = GroupChatId.New();
        var chatB = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatA, 1, 50, 30, T0), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatA, 2, 50, 30, T0 + TimeSpan.FromMinutes(5)), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatB, 1, 20, 10, T0 + TimeSpan.FromHours(2)), false));

        // act
        var conversations = await TestWait.When(async ct => {
            var c = await Coach.ListOwnConversations(tester.Session, 10, null, ct);
            c.Should().HaveCount(2);
            return c;
        });

        // assert
        conversations[0].ChatId.Should().Be(chatB);
        conversations[1].Words.Should().Be(100);
    }

    [Fact]
    public async Task ListOwnConversationsShouldSplitAMixedRunAndFilterByLanguage()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 50, 30, T0), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 2, 70, 40, T0 + TimeSpan.FromMinutes(2), language: Languages.Russian), false));

        // act
        var all = await TestWait.When(async ct => {
            var c = await Coach.ListOwnConversations(tester.Session, 10, null, ct);
            c.Should().HaveCount(2);
            return c;
        });
        var russian = await Coach.ListOwnConversations(tester.Session, 10, "ru-RU", default);
        var english = await Coach.ListOwnConversations(tester.Session, 10, "en", default);

        // assert
        all.Select(c => c.Language).Should().BeEquivalentTo(["en", "ru"]);
        russian.Should().ContainSingle().Which.Words.Should().Be(70);
        english.Should().ContainSingle().Which.Words.Should().Be(50);
    }

    [Fact]
    public async Task ExcludingAConversationShouldTakeItOutOfTheScoresButKeepItListed()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var friends = GroupChatId.New();
        var work = GroupChatId.New();
        var commander = AppHost.Services.Commander();
        var day = UsageDay.DayOf(T0);
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, friends, 1, 50, 30, T0, 2, "like"), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, friends, 2, 50, 30, T0 + TimeSpan.FromMinutes(5), 1, "like"), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, work, 1, 20, 10, T0 + TimeSpan.FromHours(2)), false));
        await WhenDay(account.Id, day, d => d.Entries == 3);

        // act
        await commander.Call(new Coach_ExcludeConversation {
            Session = tester.Session, ChatId = friends, StartEntryLid = 1, Language = "en", IsExcluded = true,
        });

        // assert
        var excluded = await WhenDay(account.Id, day, d => d.Entries == 1);
        excluded.Words.Should().Be(20);
        var conversations = await Coach.ListOwnConversations(tester.Session, 10, null, default);
        conversations.Should().HaveCount(2);
        conversations.Single(c => c.ChatId == friends).IsExcluded.Should().BeTrue();
        conversations.Single(c => c.ChatId == work).IsExcluded.Should().BeFalse();
        var range = new Range<Moment>(T0 - TimeSpan.FromDays(1), T0 + TimeSpan.FromDays(1));
        (await Backend.ListOccurrences(account.Id, "like", range, 10, default)).Should().BeEmpty();

        // act
        await commander.Call(new Coach_ExcludeConversation {
            Session = tester.Session, ChatId = friends, StartEntryLid = 1, Language = "en", IsExcluded = false,
        });

        // assert
        var restored = await WhenDay(account.Id, day, d => d.Entries == 3);
        restored.Words.Should().Be(120);
    }

    [Fact]
    public async Task AnExcludedConversationShouldStayExcludedWhenItsRowsAreSentAgainOrItContinues()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var commander = AppHost.Services.Commander();
        var day = UsageDay.DayOf(T0);
        var other = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 50, 30, T0), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, other, 1, 20, 10, T0 + TimeSpan.FromHours(3)), false));
        await WhenDay(account.Id, day, d => d.Entries == 2);
        await commander.Call(new Coach_ExcludeConversation {
            Session = tester.Session, ChatId = chatId, StartEntryLid = 1, Language = "en", IsExcluded = true,
        });
        await WhenDay(account.Id, day, d => d.Entries == 1);

        // act
        var resent = Entry(account.Id, chatId, 1, 50, 30, T0, 3, "um") with { Version = 2 };
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(resent, false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 2, 40, 20, T0 + TimeSpan.FromMinutes(10)), false));
        await Queues.WhenProcessing(TimeSpan.FromSeconds(1), default);

        // assert
        var conversations = await TestWait.When(async ct => {
            var c = await Coach.ListOwnConversations(tester.Session, 10, null, ct);
            c.Single(x => x.ChatId == chatId).Words.Should().Be(90);
            return c;
        });
        conversations.Single(c => c.ChatId == chatId).IsExcluded.Should().BeTrue();
        var days = await Backend.ListDays(
            account.Id, new Range<Moment>(day, day + TimeSpan.FromDays(1)), null, default);
        days.Should().ContainSingle().Which.Words.Should().Be(20);
    }

    [Fact]
    public async Task ARunArrivingAfterItsConversationWasExcludedShouldStayOutOfTheScores()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var friends = GroupChatId.New();
        var work = GroupChatId.New();
        var day = UsageDay.DayOf(T0);
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, friends, 1, 50, 30, T0), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, work, 1, 20, 10, T0 + TimeSpan.FromHours(3)), false));
        await WhenDay(account.Id, day, d => d.Entries == 2);
        await AppHost.Services.Commander().Call(new Coach_ExcludeConversation {
            Session = tester.Session, ChatId = friends, StartEntryLid = 1, Language = "en", IsExcluded = true,
        });
        await WhenDay(account.Id, day, d => d.Entries == 1);

        // act: the chat side emits the run only once the conversation has been quiet for a while
        await Queues.Enqueue(new CoachConversationAnalyzedEvent(
            Run(account.Id, friends, 1, T0 + TimeSpan.FromMinutes(11))));
        await Queues.Enqueue(new CoachConversationAnalyzedEvent(
            Run(account.Id, work, 1, T0 + TimeSpan.FromHours(3) + TimeSpan.FromMinutes(11))));

        // assert
        var range = new Range<Moment>(day, day + TimeSpan.FromDays(1));
        var counted = await TestWait.When(async ct => {
            var english = (await Backend.ListDays(account.Id, range, "en", ct)).Single(d => d.Language == "en");
            english.Runs.Should().BeGreaterThan(0);
            return english;
        });
        await Queues.WhenProcessing(TimeSpan.FromSeconds(1), default);
        counted = (await Backend.ListDays(account.Id, range, "en", default)).Single(d => d.Language == "en");
        counted.Runs.Should().Be(1, "only the run of the conversation that still counts");
        counted.OwnSpeechSeconds.Should().Be(30);
    }

    [Fact]
    public async Task ARunThatMovesToTheNextDayShouldLeaveItsOldDay()
    {
        // arrange: a long conversation is analysed before midnight and again after it
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var day = UsageDay.DayOf(T0);
        var lateEvening = day + TimeSpan.FromHours(23.5);
        var afterMidnight = day + TimeSpan.FromHours(24.5);
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 50, 30, lateEvening), false));
        await Queues.Enqueue(new CoachConversationAnalyzedEvent(Run(account.Id, chatId, 1, lateEvening)));
        var range = new Range<Moment>(day, day + TimeSpan.FromDays(2));
        await TestWait.When(async ct => {
            var all = await Backend.ListDays(account.Id, range, "en", ct);
            all.Should().Contain(d => d.Language == "en" && d.Runs == 1);
        });

        // act
        await Queues.Enqueue(new CoachConversationAnalyzedEvent(
            Run(account.Id, chatId, 1, afterMidnight) with { Version = 2 }));

        // assert
        var days = await TestWait.When(async ct => {
            var all = await Backend.ListDays(account.Id, range, "en", ct);
            all.Should().Contain(d => d.Day == day + TimeSpan.FromDays(1) && d.Runs == 1);
            all.Where(d => d.Day == day).Should().OnlyContain(d => d.Runs == 0, "the run left its old day");
            return all;
        });
        days.Single(d => d.Day == day).Entries.Should().Be(1);
    }

    [Fact]
    public async Task FocusAndLevelCommandsShouldUpdateSettingsAndDeleteShouldClearEverything()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var commander = AppHost.Services.Commander();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 1, 300, 200, T0, 20, "like"), false));
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Words == 300);

        // act
        await commander.Call(new Coach_SetLanguageLevel {
            Session = tester.Session,
            Language = "en",
            Level = CoachLanguageLevel.Learning,
        });
        await commander.Call(new Coach_SetFocus {
            Session = tester.Session,
            Language = "en",
            Kind = CoachMetricKind.Pace,
        });
        var focus = await Coach.GetOwnFocus(tester.Session, "en-US", default);
        var languages = await Coach.ListOwnLanguages(tester.Session, default);
        await commander.Call(new Coach_DeleteOwnData { Session = tester.Session });

        // assert
        focus.Should().Be(CoachMetricKind.Pace);
        languages.Should().Contain(l => l.Iso == "en" && l.Level == CoachLanguageLevel.Learning);
        await TestWait.When(async ct => {
            var summary = await Coach.GetOwnSummary(tester.Session, CoachWindow.AllTime, null, ct);
            summary.Words.Should().Be(0);
        });
        (await Kvas.ForUser(account.Id).UserCoachSettings().Get(default)).FocusByLanguage.Should().BeEmpty();
    }

    [Fact]
    public async Task WeekScoresShouldFollowTheLanguageTheCallerAsksFor()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var now = Clocks.SystemClock.Now;
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 1, 400, 160, now - TimeSpan.FromMinutes(1), language: Languages.Russian), false));
        await WhenDay(account.Id, UsageDay.DayOf(now), d => d.Words == 400);

        // act
        var russian = await Coach.ListOwnWeekScores(tester.Session, 4, "ru", default);
        var english = await Coach.ListOwnWeekScores(tester.Session, 4, "en", default);

        // assert
        russian[^1].Score.Should().NotBeNull();
        english[^1].Score.Should().BeNull("the English rows hold no words");
    }

    [Theory]
    [InlineData(null, "", true)]
    [InlineData("", "", true)]
    [InlineData(null, "en", false)]
    [InlineData(null, "de", true)]
    [InlineData("en-US", "ru", false)]
    public async Task WeekDeltasShouldUseOneEffectiveLanguageForBothPeriods(
        string? language, string selectedLanguage, bool isRussian)
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var now = Clocks.SystemClock.Now;
        var previousWeek = CoachWeek.StartOf(UsageDay.DayOf(now)) - TimeSpan.FromDays(7);
        var kvas = Kvas.ForUser(account.Id);
        await kvas.UserLanguageSettings().Set(new UserLanguageSettings {
            Primary = Languages.English,
            Secondary = Languages.Russian,
        });
        await kvas.UserCoachSettings().Update(x => x with {
            SelectedLanguage = selectedLanguage,
            Languages = new ApiMap<string, CoachLanguageLevel>(new Dictionary<string, CoachLanguageLevel> {
                ["ru"] = CoachLanguageLevel.Learning,
            }),
        });
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 1, 1000, 600, previousWeek, 200), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 2, 1000, 600, previousWeek, 100, language: Languages.Russian), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 3, 1000, 400, now, 5), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 4, 2000, 1200, now, 100, language: Languages.Russian), false));
        await TestWait.When(async ct => {
            var summary = await Coach.GetOwnSummary(tester.Session, CoachWindow.AllTime, null, ct);
            summary.Entries.Should().Be(4);
        });

        // act
        var deltas = await Coach.GetOwnWeekDeltas(tester.Session, language, default);

        // assert
        var fillers = deltas.Single(d => d.Kind == CoachMetricKind.Fillers);
        fillers.Current.Should().BeApproximately(isRussian ? 0.05 : 0.005, 1e-9);
        fillers.Previous.Should().BeApproximately(isRussian ? 0.1 : 0.2, 1e-9);
        fillers.IsBetter.Should().BeTrue();
        var pace = deltas.Single(d => d.Kind == CoachMetricKind.Pace);
        pace.Current.Should().Be(isRussian ? 100 : 150);
        pace.Band.Should().Be(CoachBand.Good, "the effective language also determines the pace range");
        deltas.Any(d => d.Kind == CoachMetricKind.Vocabulary).Should().Be(isRussian);
        deltas.Select(d => d.Kind).Should().Contain([
            CoachMetricKind.Fillers, CoachMetricKind.Pace, CoachMetricKind.WeakWords,
        ]);
    }

    [Fact]
    public async Task WeekDeltasShouldFallBackToThePrimaryLanguageWithoutRecentSpeech()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var kvas = Kvas.ForUser(account.Id);
        await kvas.UserLanguageSettings().Set(new UserLanguageSettings { Primary = Languages.Russian });
        await kvas.UserCoachSettings().Update(x => x with {
            Languages = new ApiMap<string, CoachLanguageLevel>(new Dictionary<string, CoachLanguageLevel> {
                ["ru"] = CoachLanguageLevel.Learning,
            }),
        });

        // act
        var deltas = await Coach.GetOwnWeekDeltas(tester.Session, null, default);

        // assert
        deltas.Should().OnlyContain(d => d.Current == null && d.Previous == null && d.IsBetter == null);
        deltas.Select(d => d.Kind).Should().Contain(CoachMetricKind.Vocabulary);
    }

    [Fact]
    public async Task WeekDeltasShouldUpdateAfterLanguageSelectionChanges()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var now = Clocks.SystemClock.Now;
        var kvas = Kvas.ForUser(account.Id);
        await kvas.UserLanguageSettings().Set(new UserLanguageSettings {
            Primary = Languages.English,
            Secondary = Languages.Russian,
        });
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 1, 1000, 400, now, 5), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 2, 2000, 1200, now, 100, language: Languages.Russian), false));
        await TestWait.When(async ct => {
            var deltas = await Coach.GetOwnWeekDeltas(tester.Session, null, ct);
            deltas.Single(d => d.Kind == CoachMetricKind.Fillers).Current.Should().BeApproximately(0.05, 1e-9);
        });

        // act
        await kvas.UserCoachSettings().Update(x => x with { SelectedLanguage = "en" });

        // assert
        await TestWait.When(async ct => {
            var deltas = await Coach.GetOwnWeekDeltas(tester.Session, null, ct);
            deltas.Single(d => d.Kind == CoachMetricKind.Fillers).Current.Should().BeApproximately(0.005, 1e-9);
        });
    }

    [Fact]
    public async Task WeekDeltasShouldUpdateAfterAConversationIsExcludedAndRestored()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var friends = GroupChatId.New();
        var work = GroupChatId.New();
        var now = Clocks.SystemClock.Now;
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, friends, 1, 1000, 500, now, 100), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, work, 1, 1000, 500, now), false));
        await TestWait.When(async ct => {
            var deltas = await Coach.GetOwnWeekDeltas(tester.Session, "en", ct);
            deltas.Single(d => d.Kind == CoachMetricKind.Fillers).Current.Should().BeApproximately(0.05, 1e-9);
        });
        var command = new Coach_ExcludeConversation {
            Session = tester.Session,
            ChatId = friends,
            StartEntryLid = 1,
            Language = "en",
            IsExcluded = true,
        };

        // act
        await AppHost.Services.Commander().Call(command);

        // assert
        await TestWait.When(async ct => {
            var deltas = await Coach.GetOwnWeekDeltas(tester.Session, "en", ct);
            deltas.Single(d => d.Kind == CoachMetricKind.Fillers).Current.Should().Be(0);
        });

        // act
        await AppHost.Services.Commander().Call(new Coach_ExcludeConversation {
            Session = tester.Session,
            ChatId = friends,
            StartEntryLid = 1,
            Language = "en",
            IsExcluded = false,
        });

        // assert
        await TestWait.When(async ct => {
            var deltas = await Coach.GetOwnWeekDeltas(tester.Session, "en", ct);
            deltas.Single(d => d.Kind == CoachMetricKind.Fillers).Current.Should().BeApproximately(0.05, 1e-9);
        });
    }

    [Fact]
    public async Task WeekDeltasShouldUseCalendarWeeksRatherThanRollingSevenDays()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var now = Clocks.SystemClock.Now;
        var weekStart = CoachWeek.StartOf(UsageDay.DayOf(now));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 1, 1000, 500, weekStart, 20), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 2, 1000, 500, weekStart - TimeSpan.FromSeconds(1), 50), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 3, 1000, 500, weekStart - TimeSpan.FromDays(7), 100), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 4, 1000, 500,
                weekStart - TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1)), false));
        await TestWait.When(async ct => {
            var summary = await Coach.GetOwnSummary(tester.Session, CoachWindow.AllTime, null, ct);
            summary.Entries.Should().Be(4);
        });

        // act
        var deltas = await Coach.GetOwnWeekDeltas(tester.Session, "en", default);

        // assert
        var fillers = deltas.Single(d => d.Kind == CoachMetricKind.Fillers);
        fillers.Current.Should().BeApproximately(0.02, 1e-9);
        fillers.Previous.Should().BeApproximately(0.075, 1e-9);
    }

    [Fact]
    public async Task SevenDayScoreShouldBeComparedWithThePreviousSevenDays()
    {
        // arrange: identical this week and last week, a bad stretch three weeks ago
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var now = Clocks.SystemClock.Now;
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 1, 400, 160, now - TimeSpan.FromMinutes(1)), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 2, 400, 160, now - TimeSpan.FromDays(9)), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(
            Entry(account.Id, chatId, 3, 400, 160, now - TimeSpan.FromDays(20), 60, "ну"), false));
        await TestWait.When(async ct => {
            var all = await Coach.GetOwnSummary(tester.Session, CoachWindow.AllTime, null, ct);
            all.Entries.Should().Be(3);
        });

        // act
        var summary = await Coach.GetOwnSummary(tester.Session, CoachWindow.Days7, null, default);

        // assert
        summary.Score.Should().NotBeNull();
        summary.ScoreDelta.Should().BeNull("this week equals last week; the bad stretch is older than a week");
    }
}
