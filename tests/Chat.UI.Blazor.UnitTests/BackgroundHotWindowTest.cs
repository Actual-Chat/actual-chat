using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class BackgroundHotWindowTest
{
    [Fact]
    public void ForegroundKeepsConfiguredWindow()
    {
        // act
        var hotWindow = TimeSpan.FromSeconds(60);
        var effective = PttReplyUI.GetEffectiveHotWindow(hotWindow, isBackground: false);

        // assert
        effective.Should().Be(hotWindow);
    }

    [Fact]
    public void BackgroundClampsToShortWindow()
    {
        // act
        var effective = PttReplyUI.GetEffectiveHotWindow(TimeSpan.FromSeconds(60), isBackground: true);

        // assert
        effective.Should().Be(Constants.Audio.PttReplyBackgroundHotWindow);
    }

    [Fact]
    public void BackgroundNeverExtendsShorterWindow()
    {
        // act
        var hotWindow = Constants.Audio.PttReplyBackgroundHotWindow - TimeSpan.FromSeconds(5);
        var effective = PttReplyUI.GetEffectiveHotWindow(hotWindow, isBackground: true);

        // assert
        effective.Should().Be(hotWindow);
    }
}
