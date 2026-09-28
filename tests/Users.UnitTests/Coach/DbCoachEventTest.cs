using ActualChat.Chat;
using ActualChat.Users.Db;

namespace ActualChat.Users.UnitTests.Coach;

public class DbCoachEventTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void RecordShouldSurviveDbRoundTrip()
    {
        // arrange
        var chatId = GroupChatId.New();
        var at = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);
        var record = new CoachRecord(CoachRecordKind.Entry, "src", UserId.New(), chatId, at) {
            Version = 7,
            Entry = new CoachEntryRecord(5, "ru-RU", 12, 10, 20, 3, 1, 0, 18, 1, 2.5, true, 1, 2, 1, 0,
                ApiArray.New(new SpeechSpan(SpeechSpanKind.Filler, "ну", 0, 2, ApiArray<string>.Empty))),
        };

        // act
        var restored = new DbCoachEvent(record).ToModel();

        // assert
        restored.Should().BeEquivalentTo(record);
        new DbCoachEvent(record).Version.Should().Be(7);
    }

    [Fact]
    public void RemovedRowShouldKeepNoPayload()
    {
        // arrange
        var record = new CoachRecord(CoachRecordKind.Entry, "src", UserId.New(), GroupChatId.New(), Moment.EpochStart) {
            Version = 3,
            Entry = new CoachEntryRecord(
                1, "en-US", 5, 5, 9, 1, 0, 0, 9, 0, 0, true, 0, 0, 0, 0, ApiArray<SpeechSpan>.Empty),
        };
        var dbEvent = new DbCoachEvent(record);

        // act
        dbEvent.MarkRemoved();

        // assert
        dbEvent.IsRemoved.Should().BeTrue();
        dbEvent.Payload.Should().Be("{}", "a removed message leaves no derived text behind");
        dbEvent.Version.Should().Be(3);
    }

    [Fact]
    public void DayShouldSurviveDbRoundTrip()
    {
        // arrange
        var day = new CoachDay(new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc)) {
            Entries = 3,
            Words = 120,
            TaggedWords = 100,
            Fillers = 4,
            FillerCounts = new ApiMap<string, int>(new Dictionary<string, int> { ["you know"] = 3, ["um"] = 1 }),
        };
        var dbDay = new DbCoachDay { UserId = "u1", Version = 1 };

        // act
        dbDay.UpdateFrom(day);
        var restored = dbDay.ToModel();

        // assert
        restored.Should().BeEquivalentTo(day);
    }
}
