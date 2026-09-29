using ActualChat.Users.Flows;
using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachWeeklyNoteTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly CoachScoringSettings S = new();
    private static readonly Moment WeekStart = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

    private static CoachDay Week(Moment start, int words, int fillers)
        => new (start) {
            Words = words, TaggedWords = words, FilledPauses = fillers, SpeechSeconds = words / 2.5,
            Sentences = words / 10, Entries = 3, Language = "en",
        };

    [Fact]
    public void ComposeShouldCarryTheScoreDeltaAndTheFocusDelta()
    {
        // arrange
        var settings = new UserCoachSettings {
            FocusByLanguage = new (new Dictionary<string, CoachMetricKind> { ["en"] = CoachMetricKind.Fillers }),
        };
        var best = new CoachConversation(
            GroupChatId.New(), 7, WeekStart, WeekStart, "en", null, 300, 120, 2, 0, 150, null, null, new (), new ());

        // act
        var note = CoachWeeklyNoteFlow.Compose(
            WeekStart, Week(WeekStart, 1000, 40), Week(WeekStart - TimeSpan.FromDays(7), 1000, 70), [best], settings, S);

        // assert
        note!.ScoreDelta.Should().BePositive();
        note.FocusKind.Should().Be(CoachMetricKind.Fillers);
        note.FocusDelta.Should().BeApproximately(-0.03, 1e-9);
        note.BestStartLid.Should().Be(7);
        note.IsPending.Should().BeTrue();
    }

    [Fact]
    public void ComposeShouldReturnNullForAQuietWeek()
        => CoachWeeklyNoteFlow.Compose(
                WeekStart, Week(WeekStart, 50, 1), Week(WeekStart - TimeSpan.FromDays(7), 1000, 70), [],
                new UserCoachSettings(), S)
            .Should().BeNull();

    [Fact]
    public void LastMondayShouldBeInTheUsersZoneAndDueOnlyOncePerWeek()
    {
        // arrange: Moscow is UTC+3; Tuesday 2026-09-29 12:00 UTC
        var zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(3), "test", "test");
        var now = new Moment(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));

        // act
        var due = CoachWeeklyNoteFlow.LastMondayAt(zone, TimeSpan.FromHours(9), now);

        // assert
        due.Should().Be(new Moment(new DateTime(2026, 9, 28, 6, 0, 0, DateTimeKind.Utc)), "Monday 09:00 local");
        CoachWeeklyNoteFlow.IsDue(due, default, now).Should().BeTrue();
        CoachWeeklyNoteFlow.IsDue(due, due + TimeSpan.FromMinutes(1), now).Should().BeFalse("already sent this week");
        CoachWeeklyNoteFlow.LastMondayAt(zone, TimeSpan.FromHours(9),
            new Moment(new DateTime(2026, 9, 28, 5, 0, 0, DateTimeKind.Utc))).Should()
            .Be(new Moment(new DateTime(2026, 9, 21, 6, 0, 0, DateTimeKind.Utc)), "before 09:00 local it is still last week");
    }
}
