using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class IsActuallyConversingTest
{
    private static readonly AudioSettings Settings = new();
    private static readonly AuthorId OwnAuthorId = AuthorId.Parse("052w3sgrad:1");
    private static readonly AuthorId OtherAuthorId = AuthorId.Parse("052w3sgrad:2");

    [Fact]
    public void NoStatsIsNotConversing()
        => ChatAudioUI.IsActuallyConversing(null, OwnAuthorId, false, Settings).Should().BeFalse();

    [Fact]
    public void YoungSessionIsConversing()
    {
        // arrange - nothing said yet, but the session is too young to judge
        var stats = new ConversationStats { Duration = Settings.ConversationMinAge - TimeSpan.FromSeconds(1) };

        // act & assert
        ChatAudioUI.IsActuallyConversing(stats, OwnAuthorId, false, Settings).Should().BeTrue();
    }

    [Fact]
    public void OwnSpeechDoesNotCount()
    {
        // arrange
        var stats = NewStats(speechDurations: new() {
            [OwnAuthorId] = Settings.SpeechDurationThreshold.TotalSeconds * 10,
        });

        // act & assert
        ChatAudioUI.IsActuallyConversing(stats, OwnAuthorId, false, Settings).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    public void RemoteSpeechCrossesTheDurationThreshold(double offset, bool expected)
    {
        // arrange
        var stats = NewStats(speechDurations: new() {
            [OtherAuthorId] = Settings.SpeechDurationThreshold.TotalSeconds + offset,
        });

        // act & assert
        ChatAudioUI.IsActuallyConversing(stats, OwnAuthorId, false, Settings).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    public void RemoteTextCrossesTheSizeThreshold(int offset, bool expected)
    {
        // arrange
        var stats = NewStats(transcriptSizes: new() {
            [OtherAuthorId] = Settings.TranscriptSizeThreshold + offset,
        });

        // act & assert
        ChatAudioUI.IsActuallyConversing(stats, OwnAuthorId, true, Settings).Should().Be(expected);
    }

    [Fact]
    public void TranscriptionOnIgnoresSpeechDuration()
    {
        // arrange - a wide-open mic that transcribed nothing is noise, not conversation
        var stats = NewStats(speechDurations: new() {
            [OtherAuthorId] = Settings.SpeechDurationThreshold.TotalSeconds * 10,
        });

        // act & assert
        ChatAudioUI.IsActuallyConversing(stats, OwnAuthorId, true, Settings).Should().BeFalse();
    }

    [Fact]
    public void TranscriptionOffIgnoresTranscriptSize()
    {
        // arrange
        var stats = NewStats(transcriptSizes: new() {
            [OtherAuthorId] = Settings.TranscriptSizeThreshold * 10,
        });

        // act & assert
        ChatAudioUI.IsActuallyConversing(stats, OwnAuthorId, false, Settings).Should().BeFalse();
    }

    [Fact]
    public void MissingOwnAuthorCountsEverySpeaker()
    {
        // arrange
        var stats = NewStats(speechDurations: new() {
            [OwnAuthorId] = Settings.SpeechDurationThreshold.TotalSeconds * 10,
        });

        // act & assert
        ChatAudioUI.IsActuallyConversing(stats, null, false, Settings).Should().BeTrue();
    }

    // Private methods

    private static ConversationStats NewStats(
        Dictionary<AuthorId, double>? speechDurations = null,
        Dictionary<AuthorId, int>? transcriptSizes = null)
        => new() {
            Duration = Settings.ConversationMinAge + TimeSpan.FromSeconds(1),
            SpeechDurations = new ApiMap<AuthorId, double>(speechDurations ?? []),
            TranscriptSizes = new ApiMap<AuthorId, int>(transcriptSizes ?? []),
        };
}
