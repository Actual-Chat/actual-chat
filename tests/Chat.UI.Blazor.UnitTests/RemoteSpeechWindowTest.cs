using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class RemoteSpeechWindowTest
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(10);
    private static readonly Moment T0 = new(TimeSpan.FromHours(1));

    [Fact]
    public void SilenceIsNotAConversation()
    {
        // arrange
        var window = NewWindow();

        // act
        window.Update(false, T0);

        // assert
        window.IsConversation(T0 + TimeSpan.FromMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void SpeakingRightNowIsAConversation()
    {
        // arrange
        var window = NewWindow();

        // act - well short of the threshold, but audible either way
        window.Update(true, T0);

        // assert
        window.IsConversation(T0 + TimeSpan.FromSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void OneShortUtteranceIsNotAConversation()
    {
        // arrange
        var window = NewWindow();

        // act
        window.Update(true, T0);
        window.Update(false, T0 + TimeSpan.FromSeconds(2));

        // assert
        window.IsConversation(T0 + TimeSpan.FromSeconds(3)).Should().BeFalse();
    }

    [Fact]
    public void UtterancesAccumulateToTheThreshold()
    {
        // arrange
        var window = NewWindow();
        var at = T0;

        // act - 4 x 3s of speech spread over a minute
        for (var i = 0; i < 4; i++) {
            window.Update(true, at);
            window.Update(false, at + TimeSpan.FromSeconds(3));
            at += TimeSpan.FromSeconds(15);
        }

        // assert
        window.IsConversation(at).Should().BeTrue();
    }

    [Fact]
    public void SpeechOlderThanTheWindowIsForgotten()
    {
        // arrange
        var window = NewWindow();
        window.Update(true, T0);
        window.Update(false, T0 + TimeSpan.FromSeconds(30));
        window.IsConversation(T0 + TimeSpan.FromSeconds(31)).Should().BeTrue();

        // act
        var afterWindow = T0 + TimeSpan.FromSeconds(30) + Window + TimeSpan.FromSeconds(1);

        // assert
        window.IsConversation(afterWindow).Should().BeFalse();
    }

    [Fact]
    public void PartiallyExpiredSpeechCountsOnlyItsRecentPart()
    {
        // arrange - a single 30s utterance ending right at the window edge
        var window = NewWindow();
        window.Update(true, T0);
        window.Update(false, T0 + TimeSpan.FromSeconds(30));

        // act - only 5s of that utterance is still inside the window
        var now = T0 + TimeSpan.FromSeconds(25) + Window;

        // assert
        window.IsConversation(now).Should().BeFalse();
    }

    [Fact]
    public void ResetForgetsEverything()
    {
        // arrange
        var window = NewWindow();
        window.Update(true, T0);
        window.Update(false, T0 + TimeSpan.FromSeconds(30));

        // act
        window.Reset();

        // assert
        window.IsConversation(T0 + TimeSpan.FromSeconds(31)).Should().BeFalse();
        window.IsSpeaking.Should().BeFalse();
    }

    private static RemoteSpeechWindow NewWindow()
        => new(Window, Threshold);
}
