using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class BackgroundHotWindowTest
{
    [Fact]
    public void ForegroundKeepsConfiguredWindow()
    {
        // act
        var hotWindow = TimeSpan.FromSeconds(60);
        var effective = WalkieTalkieReplyUI.GetEffectiveHotWindow(hotWindow, isBackground: false);

        // assert
        effective.Should().Be(hotWindow);
    }

    [Fact]
    public void BackgroundClampsToShortWindow()
    {
        // act
        var effective = WalkieTalkieReplyUI.GetEffectiveHotWindow(TimeSpan.FromSeconds(60), isBackground: true);

        // assert
        effective.Should().Be(Constants.Audio.WalkieTalkieReplyBackgroundHotWindow);
    }

    [Fact]
    public void BackgroundNeverExtendsShorterWindow()
    {
        // act
        var hotWindow = Constants.Audio.WalkieTalkieReplyBackgroundHotWindow - TimeSpan.FromSeconds(5);
        var effective = WalkieTalkieReplyUI.GetEffectiveHotWindow(hotWindow, isBackground: true);

        // assert
        effective.Should().Be(hotWindow);
    }
}
