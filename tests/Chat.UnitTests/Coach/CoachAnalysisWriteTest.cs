using ActualChat.Audio;
using ActualChat.Chat.Coach;
using ActualChat.Chat.Db;
using ActualLab.Resilience;

namespace ActualChat.Chat.UnitTests.Coach;

public sealed class CoachAnalysisWriteTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PaceUpdatesShouldRetryAfterConcurrentTagging(bool hasMeasurement)
    {
        // arrange
        var entry = Analysis();
        var measured = entry with { Pace = Measurement() };
        var previous = hasMeasurement ? measured : entry;
        var update = hasMeasurement ? entry : measured;
        var stored = new DbCoachEntry(previous with {
            Version = previous.Version + 1,
            TagState = CoachTagState.Tagged,
        });

        // act
        var write = () => CoachAnalysisBackend.IsStillWorthWriting(
            stored, update, entry.ContentHash, paceChanged: true);
        var refreshed = update with { Version = stored.Version };

        // assert
        write.Should().Throw<PostponeException>();
        CoachAnalysisBackend.IsStillWorthWriting(stored, refreshed, entry.ContentHash, paceChanged: true)
            .Should().BeTrue();
    }

    [Fact]
    public void RetaggingShouldPreservePaceOnlyForTheSameText()
    {
        // arrange
        var measured = Analysis() with { Pace = Measurement() };
        var stored = new DbCoachEntry(measured);
        var tagged = measured with { Pace = null, TagState = CoachTagState.Tagged };
        var edited = tagged with { ContentHash = ChatEntryHashExt.GetContentHashString("edited text") };

        // act
        var unchanged = CoachAnalysisBackend.WithStoredPace(tagged, stored);
        var changed = CoachAnalysisBackend.WithStoredPace(edited, stored);

        // assert
        unchanged.Pace.Should().BeEquivalentTo(measured.Pace);
        changed.Pace.Should().BeNull();
        changed.ContentHash.Should().Be(edited.ContentHash);
        changed.TagState.Should().Be(CoachTagState.Tagged);
        CoachAnalysisBackend.IsStillWorthWriting(stored, edited, edited.ContentHash).Should().BeTrue();
    }

    [Fact]
    public void StaleSpeechShouldNotBeRetriedOrWritten()
    {
        // arrange
        var entry = Analysis();
        var stored = new DbCoachEntry(entry with { Version = entry.Version + 1 });
        var currentHash = ChatEntryHashExt.GetContentHashString("edited text");

        // act
        var canWrite = CoachAnalysisBackend.IsStillWorthWriting(stored, entry, currentHash, paceChanged: true);

        // assert
        canWrite.Should().BeFalse();
    }

    private static CoachEntryAnalysis Analysis()
    {
        var chatId = GroupChatId.New();
        return new CoachEntryAnalysis(ChatEntryId.New(chatId, 1), 10) {
            AuthorId = AuthorId.New(chatId, 1),
            UserId = UserId.New(),
            ContentHash = ChatEntryHashExt.GetContentHashString("one two three four five"),
        };
    }

    private static SpeechPaceMeasurement Measurement()
        => new(1, 5_000, new SpeechPaceAnalysis([
            new SpeechPaceSegment((0, 23), (0, 5_000), 5),
        ], 5, 0, 0, 0, 0, 0));
}
