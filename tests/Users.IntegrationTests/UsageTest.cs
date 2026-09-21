using ActualChat.Live;
using ActualChat.Queues;
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
        var summary = await ComputedTest.When(async ct => {
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
        await ComputedTest.When(async ct => {
            var summary = await Backend.GetSummary(account.Id, ct);
            summary.LiveSessions.Should().Be(1);
        });
        await Task.Delay(500);
        (await Backend.GetSummary(account.Id, default)).LiveSessions.Should().Be(1, "the second delivery is a no-op");
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
        await ComputedTest.When(async ct => {
            var summary = await Backend.GetSummary(account.Id, ct);
            summary.ActiveDays.Should().Be(1);
        });
    }
}
