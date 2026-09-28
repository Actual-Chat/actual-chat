using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using ActualChat.Live;
using ActualChat.Queues;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Generators;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class UsageTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IUsageBackend Backend => AppHost.Services.GetRequiredService<IUsageBackend>();
    private IUsage Usage => AppHost.Services.GetRequiredService<IUsage>();
    private DbHub<UsersDbContext> DbHub => AppHost.Services.GetRequiredService<DbHub<UsersDbContext>>();
    private ISessionTemporalsBackend SessionTemporals => AppHost.Services.GetRequiredService<ISessionTemporalsBackend>();

    [Fact(Timeout = 90_000)]
    public async Task MessagesAndSpeechShouldBeCounted()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(false);
        var author = await tester.Authors.GetOwn(tester.Session, chatId, default);
        var now = Clocks.SystemClock.Now;

        // act
        var textEntry = await tester.CreateTextEntry(chatId, "Hi there");
        var voiceEntry = await Commander.Call(new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chatId, 0),
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = author!.Id,
                Content = "Spoken",
                Audio = new ChatEntryAudio { StreamId = $"test-audio-{RandomStringGenerator.Default.Next()}" },
                BeginsAt = now,
            })));
        await Commander.Call(new ChatsBackend_ChangeEntry(
            voiceEntry.Id,
            voiceEntry.Version,
            Change.Update(new ChatEntryDiff { EndsAt = now + TimeSpan.FromSeconds(7) })));

        // assert
        var summary = await TestWait.When(async ct => {
            var s = await Backend.GetSummary(account.Id, ct);
            s.Messages.Should().Be(1);
            s.SpeechEntries.Should().Be(1);
            return s;
        });
        summary.SpeechMs.Should().Be(7_000);
        summary.ActiveDays.Should().Be(1, "both entries land on the same UTC day");

        // act - redelivery of the very same events must not double count
        var range = new Range<Moment>(now - TimeSpan.FromDays(1), now + TimeSpan.FromDays(1));
        var days = await Backend.ListDays(account.Id, range, default);
        await Commander.Call(new UsageBackend_Record(account.Id, ApiArray.New(
            new UsageEvent(UsageEventKind.Message, now, textEntry.Id.Value, 1),
            new UsageEvent(UsageEventKind.Speech, now, voiceEntry.Id.Value, 7_000))));
        var summary2 = await Backend.GetSummary(account.Id, default);

        // assert
        days.Should().ContainSingle();
        summary2.Messages.Should().Be(1);
        summary2.SpeechMs.Should().Be(7_000);
    }

    [Fact(Timeout = 90_000)]
    public async Task LiveSessionEndShouldCountEachParticipantOnce()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(false);
        var author = await tester.Authors.GetOwn(tester.Session, chatId, default);
        var startedAt = Clocks.SystemClock.Now - TimeSpan.FromMinutes(10);
        var ended = new LiveSessionEndedEvent(
            chatId,
            startedAt,
            startedAt + TimeSpan.FromMinutes(5),
            LiveSessionKind.Ambient,
            ApiArray.New(new LiveSessionEndedMember(author!.Id, startedAt)));

        // act
        await Queues.Enqueue(ended);
        await Queues.Enqueue(ended);

        // assert
        await TestWait.When(async ct => {
            var summary = await Backend.GetSummary(account.Id, ct);
            summary.LiveSessions.Should().Be(1);
        });
        await Task.Delay(500);
        (await Backend.GetSummary(account.Id, default)).LiveSessions.Should().Be(1, "the second delivery is a no-op");
    }

    [Fact(Timeout = 90_000)]
    public async Task ARealCallShouldEarnThePromptForItsParticipants()
    {
        // arrange - nothing is enqueued by hand here: the call closes through LiveSessionsBackend
        await using var tester = AppHost.NewWebClientTester(Out);
        var bob = await tester.SignInAsUniqueBob();
        await using var otherTester = AppHost.NewWebClientTester(Out);
        var alice = await otherTester.SignInAsUniqueAlice();
        var chatId = (ChatId)PeerChatId.New(bob.Id, alice.Id);
        var authors = AppHost.Services.GetRequiredService<IAuthorsBackend>();
        var bobAuthor = await authors.EnsureJoined(chatId, bob.Id, default);
        var aliceAuthor = await authors.EnsureJoined(chatId, alice.Id, default);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        // act
        await liveBackend.StartCall(chatId, bobAuthor.Id, ApiArray.New(aliceAuthor.Id), false, default);
        await liveBackend.AcceptCall(chatId, aliceAuthor.Id, default);
        await liveBackend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, true, default);
        await liveBackend.SetParticipation(chatId, bobAuthor.Id, ParticipationKind.Record, true, default);
        await liveBackend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, false, default);
        await liveBackend.SetParticipation(chatId, bobAuthor.Id, ParticipationKind.Record, false, default);

        // assert - the close counted the session for both and decided the prompt in the same handler
        var pending = await TestWait.When(async ct => {
            var p = await Usage.GetPendingReviewPrompt(tester.Session, ct);
            p.Should().NotBeNull();
            return p!;
        }, TimeSpan.FromSeconds(20));
        pending.ChatId.Should().Be(chatId);
        (await Backend.GetSummary(bob.Id, default)).LiveSessions.Should().Be(1);
        await TestWait.When(async ct => {
            (await Backend.GetSummary(alice.Id, ct)).LiveSessions.Should().Be(1);
            (await Usage.GetPendingReviewPrompt(otherTester.Session, ct)).Should().NotBeNull();
        });

        // act - recording the outcome on one device clears it everywhere
        await tester.Commander.Call(new Usage_RecordReviewPrompt {
            Session = tester.Session,
            Outcome = ReviewPromptOutcome.Asked,
        });

        // assert
        (await Usage.GetPendingReviewPrompt(tester.Session, default)).Should().BeNull();
        (await Usage.GetPendingReviewPrompt(otherTester.Session, default)).Should().NotBeNull(
            "Alice's prompt is her own");
    }

    [Fact(Timeout = 90_000)]
    public async Task CheckInShouldMarkTheDayActive()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();

        // act
        await tester.Commander.Call(new UserPresences_CheckIn { Session = tester.Session, IsActive = true });
        await tester.Commander.Call(new UserPresences_CheckIn { Session = tester.Session, IsActive = true });

        // assert
        await TestWait.When(async ct => {
            var summary = await Backend.GetSummary(account.Id, ct);
            summary.ActiveDays.Should().Be(1);
        });
    }

    [Fact(Timeout = 90_000)]
    public async Task ReviewPromptShouldFollowUsageAndHistory()
    {
        // arrange - the fixture lowers every threshold except the live-session one
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(false);
        var author = await tester.Authors.GetOwn(tester.Session, chatId, default);
        var startedAt = Clocks.SystemClock.Now - TimeSpan.FromMinutes(10);

        // act
        var before = await Usage.GetReviewPromptState(tester.Session, default);
        await Queues.Enqueue(new LiveSessionEndedEvent(
            chatId,
            startedAt,
            startedAt + TimeSpan.FromMinutes(5),
            LiveSessionKind.Call,
            ApiArray.New(new LiveSessionEndedMember(author!.Id, startedAt))));

        // assert - the session is the first activity, so it also supplies the one active day,
        // and the handler that counts it also decides the prompt
        before.CanPrompt.Should().BeFalse();
        var pending = await TestWait.When(async ct => {
            var p = await Usage.GetPendingReviewPrompt(tester.Session, ct);
            p.Should().NotBeNull();
            return p!;
        });
        pending.ChatId.Should().Be(chatId);
        var eligible = await Usage.GetReviewPromptState(tester.Session, default);
        eligible.CanPrompt.Should().BeTrue(eligible.Reason);

        // act - an unconfirmed ask clears the pending prompt and defers, a confirmed review ends it
        await tester.Commander.Call(new Usage_RecordReviewPrompt {
            Session = tester.Session,
            Outcome = ReviewPromptOutcome.Asked,
        });
        var afterAsk = await Usage.GetReviewPromptState(tester.Session, default);
        var pendingAfterAsk = await Usage.GetPendingReviewPrompt(tester.Session, default);
        var history = await Usage.GetOwnReviewPromptHistory(tester.Session, default);
        await tester.Commander.Call(new Usage_RecordReviewPrompt {
            Session = tester.Session,
            Outcome = ReviewPromptOutcome.Reviewed,
        });
        var afterReview = await Usage.GetReviewPromptState(tester.Session, default);

        // assert
        afterAsk.CanPrompt.Should().BeFalse();
        afterAsk.Reason.Should().StartWith("Prompted recently");
        pendingAfterAsk.Should().BeNull();
        history.PromptCount.Should().Be(1);
        history.Outcome.Should().Be(ReviewPromptOutcome.Asked);
        afterReview.Reason.Should().Be("Already reviewed");
    }

    [Fact(Timeout = 90_000)]
    public async Task NonActivityKindsShouldNotCreateDayRows()
    {
        // arrange - a day nothing else touches, so any day row there comes from these events
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var at = Clocks.SystemClock.Now - TimeSpan.FromDays(10);
        var range = new Range<Moment>(at - TimeSpan.FromDays(1), at + TimeSpan.FromDays(1));

        // act
        await Commander.Call(new UsageBackend_Record(account.Id, ApiArray.New(
            UsageEventSource.OnboardingStep("Phone", true, at),
            UsageEventSource.SignUp(ArrivalInfo.New(ArrivalKind.Campaign, "c1")!.Value, at))));
        var days = await Backend.ListDays(account.Id, range, default);
        await Commander.Call(new UsageBackend_RebuildDays(account.Id));
        var rebuiltDays = await Backend.ListDays(account.Id, range, default);

        // assert
        days.Should().BeEmpty("a day row counts as an active day for the review prompt");
        rebuiltDays.Should().BeEmpty("the rebuild must apply the same rule");
    }

    [Fact(Timeout = 90_000)]
    public async Task SignUpShouldRecordTheArrivalAndConsumeIt()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await SetArrival(tester.Session, "join:inv-123");

        // act
        var account = await tester.SignInAsUniqueAlice();

        // assert
        var signUp = await WaitForSignUp(account.Id);
        signUp.SourceId.Should().Be("join:inv-123");
        signUp.Attributes!.ArrivalKind.Should().Be(ArrivalKind.Join);
        var arrivalKey = Constants.SessionTemporals.ToClientKey(Constants.SessionTemporals.ArrivalKey);
        await TestWait.When(async ct => (await SessionTemporals.Get(tester.Session, arrivalKey, ct))
            .Should().BeNull("the arrival is consumed by the sign-up"));
    }

    [Fact(Timeout = 90_000)]
    public async Task SignUpWithoutArrivalShouldFallBackToWeb()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);

        // act
        var account = await tester.SignInAsUniqueAlice();

        // assert
        var signUp = await WaitForSignUp(account.Id);
        signUp.SourceId.Should().Be("web", "a test web client has no app user agent");
    }

    [Fact(Timeout = 90_000)]
    public async Task InvalidArrivalShouldFallBackAndNotBreakSignUp()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await SetArrival(tester.Session, "join:" + new string('x', 500) + " <script>");

        // act
        var account = await tester.SignInAsUniqueAlice();

        // assert
        account.IsGuestOrNull().Should().BeFalse();
        var signUp = await WaitForSignUp(account.Id);
        signUp.SourceId.Should().Be("web");
    }

    [Fact(Timeout = 90_000)]
    public async Task ExistingAccountSignInShouldNotRecordSignUp()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        await WaitForSignUp(account.Id);
        await using var otherTester = AppHost.NewWebClientTester(Out);
        await SetArrival(otherTester.Session, "campaign:late");

        // act
        await otherTester.SignIn(account);

        // assert - the arrival is dropped by the same operation that would have recorded a sign-up
        var arrivalKey = Constants.SessionTemporals.ToClientKey(Constants.SessionTemporals.ArrivalKey);
        await TestWait.When(async ct => (await SessionTemporals.Get(otherTester.Session, arrivalKey, ct))
            .Should().BeNull("any sign-in consumes the arrival"));
        var events = await ListEvents(account.Id, UsageEventKind.SignUp, default);
        events.Should().ContainSingle().Which.SourceId.Should().Be("web", "only account creation records a sign-up");
    }

    [Fact(Timeout = 90_000)]
    public async Task SignInFromLinkShouldBeCountedOnTheServer()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await SetTemporal(tester.Session, Constants.SessionTemporals.SignInFromLinkKey, "1");
        using var funnelEvents = new FunnelEventListener();

        // act
        await tester.SignInAsUniqueAlice();

        // assert
        await TestWait.WhenPolled(() => funnelEvents.Count(FunnelEvent.SignInCompletedFromLink)
            .Should().BeGreaterThan(0, "a full-page sign-in redirect reloads the client, so only the server can count it"));
        var key = Constants.SessionTemporals.ToClientKey(Constants.SessionTemporals.SignInFromLinkKey);
        await TestWait.When(async ct => (await SessionTemporals.Get(tester.Session, key, ct)).Should().BeNull());
    }

    [Fact(Timeout = 90_000)]
    public async Task OnboardingStepShouldBeRecordedOncePerStep()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();

        // act
        await RecordStep(tester, "Phone", false);
        await RecordStep(tester, "Phone", true);
        await RecordStep(tester, OnboardingSteps.Finished, true);

        // assert
        var events = await ListEvents(account.Id, UsageEventKind.OnboardingStep, default);
        events.Should().HaveCount(2);
        events.Single(e => e.SourceId == "Phone").Value.Should().Be(0, "the first report of a step wins");
        events.Should().Contain(e => e.SourceId == OnboardingSteps.Finished);
    }

    [Fact(Timeout = 90_000)]
    public async Task UnknownOrGuestOnboardingStepShouldBeRejected()
    {
        // arrange
        await using var guestTester = AppHost.NewWebClientTester(Out);
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();

        // act
        var unknownStep = () => RecordStep(tester, "Nope", true);
        var guestStep = () => RecordStep(guestTester, "Phone", true);

        // assert
        await unknownStep.Should().ThrowAsync<Exception>();
        await guestStep.Should().ThrowAsync<Exception>();
    }

    [Fact(Timeout = 90_000)]
    public async Task FunnelEventShouldAcceptOnlyClientEvents()
    {
        // arrange - a guest on purpose: link-opened events happen before an account exists
        await using var guestTester = AppHost.NewWebClientTester(Out);

        // act
        var clientEvent = () => RecordFunnelEvent(guestTester, FunnelEvent.JoinOpenedSignedOut);
        var serverEvent = () => RecordFunnelEvent(guestTester, FunnelEvent.SignUp);
        var unknownEvent = () => RecordFunnelEvent(guestTester, (FunnelEvent)99);

        // assert
        await clientEvent.Should().NotThrowAsync();
        await serverEvent.Should().ThrowAsync<Exception>();
        await unknownEvent.Should().ThrowAsync<Exception>();
    }

    // Private methods

    private Task SetArrival(Session session, string value)
        => SetTemporal(session, Constants.SessionTemporals.ArrivalKey, value);

    private Task SetTemporal(Session session, string key, string value)
        => Commander.Call(new SessionTemporals_Set { Session = session, Key = key, Value = value });

    private Task<UsageEvent> WaitForSignUp(UserId userId)
        // Polled: the rows are read from the DB directly, so there is no invalidation to wake a When on
        => TestWait.WhenPolled<UsageEvent>(async () => {
            var events = await ListEvents(userId, UsageEventKind.SignUp, default);
            return events.Should().ContainSingle().Subject;
        });

    private async Task<List<UsageEvent>> ListEvents(
        UserId userId, UsageEventKind kind, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken);
        await using var _ = dbContext;
        var dbEvents = await dbContext.UsageEvents
            .Where(e => e.UserId == userId.Value && e.Kind == kind)
            .ToListAsync(cancellationToken);
        return dbEvents.Select(e => e.ToModel()).ToList();
    }

    private static Task RecordStep(IWebClientTester tester, string step, bool isCompleted)
        => tester.Commander.Call(new Usage_RecordOnboardingStep {
            Session = tester.Session,
            Step = step,
            IsCompleted = isCompleted,
        });

    private static Task RecordFunnelEvent(IWebClientTester tester, FunnelEvent funnelEvent)
        => tester.Commander.Call(new Usage_RecordFunnelEvent { Session = tester.Session, Event = funnelEvent });

    // Nested types

    private sealed class FunnelEventListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<string> _events = new();

        public FunnelEventListener()
        {
            _listener.InstrumentPublished = (instrument, listener) => {
                if (instrument.Name == "usage.funnel.events")
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) => {
                foreach (var tag in tags)
                    if (tag.Key == "event")
                        _events.Enqueue(tag.Value as string ?? "");
            });
            _listener.Start();
        }

        public void Dispose()
            => _listener.Dispose();

        public int Count(FunnelEvent funnelEvent)
            => _events.Count(e => e == funnelEvent.ToString());
    }
}
