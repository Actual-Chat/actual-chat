using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Serialization;
using ActualChat.Users.Db;

namespace ActualChat.Users.UnitTests.Coach;

public class SpeechPaceSummaryTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment Day = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SummaryShouldRetainAbsoluteBinsAndCoverage()
    {
        // arrange
        var measurement = Measurement();

        // act
        var summary = SpeechPaceSummary.FromMeasurement(measurement);

        // assert
        summary.Durations.Should().BeEquivalentTo(new Dictionary<int, long> { [12] = 5_000, [48] = 5_000 });
        summary.MeasuredEntries.Should().Be(1);
        summary.AudioMilliseconds.Should().Be(12_000);
        summary.ValidWords.Should().Be(25);
        summary.PauseMilliseconds.Should().Be(1_000);
        summary.UnmappedMilliseconds.Should().Be(1_000);
    }

    [Fact]
    public void DailyRowsShouldSeparateLanguagesWithoutMeasuringLegacyEntries()
    {
        // arrange
        var english = Entry(1, "en-US", Measurement());
        var russian = Entry(2, "ru", Measurement());
        var legacy = Entry(3, "en", null);

        // act
        var rows = CoachDayBuilder.BuildAll(Day, [english, russian, legacy], 20);
        var merged = CoachDayBuilder.Merge(Day, rows);

        // assert
        rows.Should().HaveCount(2);
        rows.Single(d => d.Language == "en").Entries.Should().Be(2);
        rows.Single(d => d.Language == "en").Pace!.MeasuredEntries.Should().Be(1);
        rows.Single(d => d.Language == "ru").Pace!.MeasuredEntries.Should().Be(1);
        merged.Pace!.MeasuredEntries.Should().Be(2);
        merged.Pace.AudioMilliseconds.Should().Be(24_000);
        merged.Pace.Durations[12].Should().Be(10_000);
        merged.Pace.Durations[48].Should().Be(10_000);
        merged.Words.Should().Be(75);
    }

    [Fact]
    public void MissingMeasurementShouldStayNullWhileEmptyClassificationRetainsCoverage()
    {
        // arrange
        var measurement = new SpeechPaceMeasurement(1, 2_000,
            new SpeechPaceAnalysis([], 2, 0, 2, 800, 0, 1_200));

        // act
        var missing = CoachDayBuilder.Build(Day, [Entry(1, "en", null)], 20);
        var unclassified = CoachDayBuilder.Build(Day, [Entry(2, "en", measurement)], 20);

        // assert
        missing.Pace.Should().BeNull();
        SpeechPaceSummary.Merge([]).Should().BeNull();
        unclassified.Pace!.Durations.Should().BeEmpty();
        unclassified.Pace.MeasuredEntries.Should().Be(1);
        unclassified.Pace.UnclassifiedWords.Should().Be(2);
        unclassified.Pace.UnclassifiedMilliseconds.Should().Be(800);
        unclassified.Pace.UnmappedMilliseconds.Should().Be(1_200);
    }

    [Fact]
    public void PeriodMergeShouldBeAssociativeAndNotDependOnInactiveDates()
    {
        // arrange
        var a = CoachDayBuilder.Build(Day, [Entry(1, "en", Measurement())], 20);
        var b = a with { Day = Day + TimeSpan.FromDays(10) };
        var empty = new CoachDay(Day + TimeSpan.FromDays(20));

        // act
        var direct = CoachDayBuilder.Merge(Day, [a, b, empty]);
        var nested = CoachDayBuilder.Merge(Day, [CoachDayBuilder.Merge(Day, [a, b]), empty]);

        // assert
        direct.Pace.Should().BeEquivalentTo(nested.Pace);
        direct.Pace!.MeasuredEntries.Should().Be(2);
        direct.Pace.PauseMilliseconds.Should().Be(2_000);
        direct.Pace.AudioMilliseconds.Should().Be(24_000);
    }

    [Fact]
    public void DailyStorageShouldUseBinaryWithoutDuplicatingPaceInJson()
    {
        // arrange
        var day = CoachDayBuilder.Build(Day, [Entry(1, "en", Measurement())], 20);
        var row = new DbCoachDay();

        // act
        row.UpdateFrom(day);
        var restored = row.ToModel();
        var json = SystemJsonSerializer.Default.Read<CoachDay>(SystemJsonSerializer.Default.Write(day));
        var serializer = new VersionedByteSerializer([MessagePackByteSerializer.Default]);
        using var bytes = serializer.Write(day);
        var binary = (CoachDay)serializer.Read(bytes.WrittenMemory, typeof(CoachDay), out _)!;

        // assert
        row.PaceData.Should().NotBeNull();
        row.Data.Should().NotContain("pace");
        restored.Should().BeEquivalentTo(day);
        json.Should().BeEquivalentTo(day);
        binary.Should().BeEquivalentTo(day);
        row.UpdateFrom(day with { Pace = null });
        row.PaceData.Should().BeNull();
        row.ToModel().Pace.Should().BeNull();
    }

    [Fact]
    public void LegacyDailyJsonShouldRemainReadableWithoutInventingCoverage()
    {
        // arrange
        var row = new DbCoachDay { Data = "{\"entries\":1,\"words\":25}" };

        // act
        var day = row.ToModel();

        // assert
        day.Entries.Should().Be(1);
        day.Words.Should().Be(25);
        day.Pace.Should().BeNull();
    }

    [Fact]
    public void SummaryCodecShouldRejectUnsupportedAndInconsistentData()
    {
        // arrange
        var summary = SpeechPaceSummary.FromMeasurement(Measurement());
        var serializer = new VersionedByteSerializer([MessagePackByteSerializer.Default]);
        using var unsupported = serializer.Write(summary with { Version = 2 });
        var unsupportedBytes = unsupported.ToArray();
        var trailingBytes = summary.ToBytes().Append((byte)0).ToArray();

        // act
        var invalidVersion = () => SpeechPaceSummary.FromBytes(unsupportedBytes);
        var invalidCoverage = () => (summary with { AudioMilliseconds = 1 }).ToBytes();
        var invalidBin = () => (summary with { BinWidth = 10 }).ToBytes();
        var trailing = () => SpeechPaceSummary.FromBytes(trailingBytes);
        var empty = () => SpeechPaceSummary.FromBytes(Array.Empty<byte>());

        // assert
        invalidVersion.Should().Throw<InvalidDataException>();
        invalidCoverage.Should().Throw<InvalidDataException>();
        invalidBin.Should().Throw<InvalidDataException>();
        trailing.Should().Throw<InvalidDataException>();
        empty.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SummaryMergeShouldRejectOverflowRatherThanWrapCoverage()
    {
        // arrange
        var summary = SpeechPaceSummary.FromMeasurement(Measurement()) with {
            AudioMilliseconds = long.MaxValue,
            UnmappedMilliseconds = long.MaxValue - 11_000,
        };

        // act
        var merge = () => SpeechPaceSummary.Merge([summary, summary]);

        // assert
        merge.Should().Throw<OverflowException>();
    }

    // Private methods

    private static SpeechPaceMeasurement Measurement()
        => new(1, 12_000, new SpeechPaceAnalysis([
            new SpeechPaceSegment((0, 24), (0, 5_000), 5),
            new SpeechPaceSegment((25, 124), (6_000, 11_000), 20),
        ], 25, 0, 0, 0, 1_000, 1_000));

    private static CoachRecord Entry(long lid, string language, SpeechPaceMeasurement? pace)
    {
        var chatId = GroupChatId.New();
        var analysis = new CoachEntryAnalysis(ChatEntryId.New(chatId, lid), 1) {
            AuthorId = AuthorId.New(chatId, 1),
            UserId = UserId.New(),
            BeginsAt = Day + TimeSpan.FromMinutes(lid),
            Language = Language.Parse(language),
            Words = 25,
            DurationSeconds = 12,
            SpeechSeconds = 10,
            Pace = pace,
        };
        return CoachRecord.FromEntry(analysis);
    }
}
