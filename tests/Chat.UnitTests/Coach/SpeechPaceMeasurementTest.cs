using ActualChat.Audio;
using ActualChat.Serialization;

namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechPaceMeasurementTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void MeasurementShouldRoundTripThroughBinaryAndJson()
    {
        // arrange
        var value = Measurement();

        // act
        var binary = SpeechPaceMeasurement.FromBytes(value.ToBytes());
        var json = SystemJsonSerializer.Default.Read<SpeechPaceMeasurement>(SystemJsonSerializer.Default.Write(value));

        // assert
        binary.Should().BeEquivalentTo(value);
        json.Should().BeEquivalentTo(value);
        binary.IsIdenticalTo(json).Should().BeTrue();
    }

    [Fact]
    public void EmptyClassificationShouldRetainUnavailableRate()
    {
        // arrange
        var value = new SpeechPaceMeasurement(1, 2_000, new SpeechPaceAnalysis([], 2, 0, 2, 800, 0, 1_200));

        // act
        var restored = SpeechPaceMeasurement.FromBytes(value.ToBytes());
        var distribution = SpeechPaceHistogram.Classify(restored.Analysis.Segments, 100, 180);

        // assert
        restored.Analysis.UnclassifiedWords.Should().Be(2);
        distribution.WithinRate.Should().BeNull();
    }

    [Fact]
    public void UnsupportedContractShouldNotBecomeMissingMeasurement()
    {
        // arrange
        var serializer = new VersionedByteSerializer([MessagePackByteSerializer.Default]);
        using var data = serializer.Write(Measurement() with { Version = 2 });
        var bytes = data.ToArray();

        // act
        var act = () => SpeechPaceMeasurement.FromBytes(bytes);

        // assert
        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyOrTrailingPayloadShouldBeRejected(bool trailing)
    {
        // arrange
        var bytes = trailing ? Measurement().ToBytes().Append((byte)0).ToArray() : [];

        // act
        var act = () => SpeechPaceMeasurement.FromBytes(bytes);

        // assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void InconsistentCoverageShouldBeRejected()
    {
        // arrange
        var value = Measurement();
        value = value with { Analysis = value.Analysis with { UnmappedMilliseconds = 0 } };

        // act
        var act = () => value.ToBytes();

        // assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void OverlappingSegmentsShouldBeRejected()
    {
        // arrange
        var value = new SpeechPaceMeasurement(1, 10_000, new SpeechPaceAnalysis([
            new SpeechPaceSegment((0, 10), (0, 6_000), 10),
            new SpeechPaceSegment((11, 20), (5_000, 9_000), 10),
        ], 20, 0, 0, 0, 0, 0));

        // act
        var act = () => value.ToBytes();

        // assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ContributionShouldCarryPaceWithoutChangingHeadlineMetrics()
    {
        // arrange
        var chatId = GroupChatId.New();
        var value = new CoachEntryAnalysis(ChatEntryId.New(chatId, 1), 1) {
            AuthorId = AuthorId.New(chatId, 1),
            UserId = UserId.New(),
            Language = Languages.English,
            DurationSeconds = 12,
            SpeechSeconds = 11,
            Words = 30,
            Pace = Measurement(),
        };

        // act
        var contribution = ActualChat.Users.CoachRecord.FromEntry(value);

        // assert
        contribution.Entry!.Pace.Should().BeSameAs(value.Pace);
        contribution.Entry.DurationSeconds.Should().Be(12);
        contribution.Entry.SpeechSeconds.Should().Be(11);
        contribution.Entry.Words.Should().Be(30);
    }

    // Private methods

    private static SpeechPaceMeasurement Measurement()
        => new(1, 12_000, new SpeechPaceAnalysis(
            [new SpeechPaceSegment((0, 10), (1_000, 11_000), 30)], 30, 0, 0, 0, 0, 2_000));
}
