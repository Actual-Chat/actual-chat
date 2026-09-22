using ActualChat.Live;
using ActualChat.Queues;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class UsageTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IUsageBackend Backend => AppHost.Services.GetRequiredService<IUsageBackend>();
    private IUsage Usage => AppHost.Services.GetRequiredService<IUsage>();

    [Fact(Timeout = 60_000)]
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

    [Fact(Timeout = 60_000)]
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

    [Fact(Timeout = 60_000)]
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

    [Fact(Timeout = 60_000)]
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

    [Fact(Timeout = 60_000)]
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
}
