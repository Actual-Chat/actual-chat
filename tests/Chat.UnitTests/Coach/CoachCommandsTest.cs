using ActualLab.Resilience;

namespace ActualChat.Chat.UnitTests.Coach;

public class CoachCommandsTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AnalyzeEntryShouldAcceptMetadataChangesWithoutAcceptingChangedSpeech(int format)
    {
        // arrange
        var chatId = GroupChatId.New();
        var entry = ChatEntry.NewEmpty(ChatEntryId.New(chatId, 1)) with {
            Version = 10,
            Content = "one two",
            Audio = new ChatEntryAudio {
                MediaId = MediaId.New(chatId.Value),
                TimeMap = new LinearMap(0, 1, 3, 2, 4, 3, 7, 4),
            },
        };
        var command = CoachAnalysisBackend_AnalyzeEntry.New(entry, capturePace: true);
        var restored = format switch {
            0 => SystemJsonSerializer.Default.Read<CoachAnalysisBackend_AnalyzeEntry>(
                SystemJsonSerializer.Default.Write(command)),
            1 => NewtonsoftJsonSerializer.Default.Read<CoachAnalysisBackend_AnalyzeEntry>(
                NewtonsoftJsonSerializer.Default.Write(command)),
            _ => MessagePackSerializer.Deserialize<CoachAnalysisBackend_AnalyzeEntry>(
                MessagePackSerializer.Serialize(command, MessagePackByteSerializer.DefaultOptions),
                MessagePackByteSerializer.DefaultOptions),
        };
        var metadata = entry with { Version = 11, IsThreadStart = true };
        var edited = metadata with { Content = "three four" };
        var remapped = metadata with { Audio = entry.Audio with { TimeMap = new LinearMap(0, 0, 7, 4) } };
        var replaced = metadata with { Audio = entry.Audio with { MediaId = MediaId.New(chatId.Value) } };

        // act
        var matches = new[] { entry, metadata, edited, remapped, replaced }
            .Select(restored.MatchesEntry).ToArray();

        // assert
        matches.Should().Equal(true, true, false, false, false);
        restored.SourceHash.Should().Be(command.SourceHash);
        restored.CapturePace.Should().BeTrue();
        restored.UpdatePace.Should().BeTrue();
    }

    [Fact]
    public void LegacyAnalyzeEntryShouldStillRequireTheExactRevision()
    {
        // arrange
        var entry = ChatEntry.NewEmpty(ChatEntryId.New(GroupChatId.New(), 1)) with { Version = 10 };
        var command = new CoachAnalysisBackend_AnalyzeEntry(entry.Id, false) { EntryVersion = entry.Version };

        // act
        var matches = new[] { entry, entry with { Version = 11 } }.Select(command.MatchesEntry).ToArray();

        // assert
        matches.Should().Equal(true, false);
    }

    [Fact]
    public void AnalyzeEntryShouldOutliveTheDefaultQueueTimeout()
    {
        // arrange
        var command = new CoachAnalysisBackend_AnalyzeEntry(ChatEntryId.New(GroupChatId.New(), 1), false);

        // assert
        command.Should().BeAssignableTo<IHasTimeout>("the immediate path calls the LLM inside the command");
        ((IHasTimeout)command).Timeout.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(2));
    }
}
