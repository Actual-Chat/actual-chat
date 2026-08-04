using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public class AudioSessionOwnershipTest
{
    [Fact]
    public void ActivationDuringTransmitTakesTransmitOwnership()
        => AudioSessionOwnership.OnActivated(true).Should().Be(AudioSessionOwner.PttTransmit);

    [Fact]
    public void ActivationWithoutTransmitTakesPlaybackOwnership()
        => AudioSessionOwnership.OnActivated(false).Should().Be(AudioSessionOwner.PttPlayback);

    [Theory]
    [InlineData(AudioSessionOwner.PttTransmit)]
    [InlineData(AudioSessionOwner.PttPlayback)]
    [InlineData(AudioSessionOwner.App)]
    public void DeactivationAlwaysReturnsOwnershipToTheApp(AudioSessionOwner current)
    {
        AudioSessionOwnership.OnReleased(current, AudioSessionRelease.Deactivated, false)
            .Should().Be(AudioSessionOwner.App);
        AudioSessionOwnership.OnReleased(current, AudioSessionRelease.Deactivated, true)
            .Should().Be(AudioSessionOwner.App);
    }

    [Theory]
    [InlineData(AudioSessionOwner.PttTransmit)]
    [InlineData(AudioSessionOwner.PttPlayback)]
    [InlineData(AudioSessionOwner.App)]
    public void LeavingTheChannelAlwaysReturnsOwnershipToTheApp(AudioSessionOwner current)
    {
        AudioSessionOwnership.OnReleased(current, AudioSessionRelease.ChannelLeft, false)
            .Should().Be(AudioSessionOwner.App);
        AudioSessionOwnership.OnReleased(current, AudioSessionRelease.ChannelLeft, true)
            .Should().Be(AudioSessionOwner.App);
    }

    [Fact]
    public void EndingATransmitWithNothingPlayingReturnsOwnershipToTheApp()
    {
        // act
        var fromTransmit = AudioSessionOwnership
            .OnReleased(AudioSessionOwner.PttTransmit, AudioSessionRelease.TransmitEnded, false);
        var fromPlayback = AudioSessionOwnership
            .OnReleased(AudioSessionOwner.PttPlayback, AudioSessionRelease.TransmitEnded, false);
        var fromApp = AudioSessionOwnership
            .OnReleased(AudioSessionOwner.App, AudioSessionRelease.TransmitEnded, false);

        // assert
        fromTransmit.Should().Be(AudioSessionOwner.App);
        fromPlayback.Should().Be(AudioSessionOwner.PttPlayback);
        fromApp.Should().Be(AudioSessionOwner.App);
    }

    [Fact]
    public void EndingATransmitOverALivePlaybackFallsBackToPlayback()
    {
        // act
        var fromTransmit = AudioSessionOwnership
            .OnReleased(AudioSessionOwner.PttTransmit, AudioSessionRelease.TransmitEnded, true);
        var fromPlayback = AudioSessionOwnership
            .OnReleased(AudioSessionOwner.PttPlayback, AudioSessionRelease.TransmitEnded, true);

        // assert
        // Full duplex: the framework still owns an active session for the incoming voice, and
        // handing it to the app would let the app reconfigure it underneath that voice.
        fromTransmit.Should().Be(AudioSessionOwner.PttPlayback);
        fromPlayback.Should().Be(AudioSessionOwner.PttPlayback);
    }

    [Fact]
    public void OnlyTheAppMayActivateTheSession()
    {
        AudioSessionOwnership.MayActivate(AudioSessionOwner.App).Should().BeTrue();
        AudioSessionOwnership.MayActivate(AudioSessionOwner.PttPlayback).Should().BeFalse();
        AudioSessionOwnership.MayActivate(AudioSessionOwner.PttTransmit).Should().BeFalse();
    }

    [Fact]
    public void OnlyTheAppMayConfigureTheSession()
    {
        // Under either PTT owner the framework owns category and mode - the wake cue would
        // otherwise set the session to Ambient underneath a live call.
        AudioSessionOwnership.MayConfigure(AudioSessionOwner.App).Should().BeTrue();
        AudioSessionOwnership.MayConfigure(AudioSessionOwner.PttPlayback).Should().BeFalse();
        AudioSessionOwnership.MayConfigure(AudioSessionOwner.PttTransmit).Should().BeFalse();
    }
}
