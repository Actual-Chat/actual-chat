using ActualChat.Live;

namespace ActualChat.Streaming.UnitTests;

public sealed class ListeningCatchUpTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly Moment Anchor = new(DateTime.UtcNow);

    [Fact]
    public void NoAnchorNeverCatchesUp()
    {
        // act
        var isTarget = NewStreamInfo(Anchor).IsCatchUpTarget(default);

        // assert
        isTarget.Should().BeFalse();
    }

    [Fact]
    public void StreamAtAnchorCatchesUp()
    {
        // act
        var isTarget = NewStreamInfo(Anchor).IsCatchUpTarget(Anchor);

        // assert
        isTarget.Should().BeTrue();
    }

    [Fact]
    public void StreamSlightlyBeforeAnchorCatchesUp()
    {
        // arrange
        var beginsAt = Anchor - Constants.Audio.ListeningCatchUpTolerance + TimeSpan.FromSeconds(0.5);

        // act
        var isTarget = NewStreamInfo(beginsAt).IsCatchUpTarget(Anchor);

        // assert
        isTarget.Should().BeTrue();
    }

    [Fact]
    public void StreamWellBeforeAnchorDoesNotCatchUp()
    {
        // arrange
        var beginsAt = Anchor - Constants.Audio.ListeningCatchUpTolerance - TimeSpan.FromSeconds(0.5);

        // act
        var isTarget = NewStreamInfo(beginsAt).IsCatchUpTarget(Anchor);

        // assert
        isTarget.Should().BeFalse();
    }

    [Fact]
    public void StreamAfterAnchorCatchesUp()
    {
        // act
        var isTarget = NewStreamInfo(Anchor + TimeSpan.FromSeconds(30)).IsCatchUpTarget(Anchor);

        // assert
        isTarget.Should().BeTrue();
    }

    // Private methods

    private static LiveAudioStreamInfo NewStreamInfo(Moment beginsAt)
        => new() {
            ChatId = TestChatId,
            AuthorId = AuthorId.New(TestChatId, 1),
            StreamId = "test-stream",
            BeginsAt = beginsAt,
        };
}
