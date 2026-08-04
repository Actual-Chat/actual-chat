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
        => AudioSessionOwnership.OnReleased(current, AudioSessionRelease.Deactivated)
            .Should().Be(AudioSessionOwner.App);

    [Theory]
    [InlineData(AudioSessionOwner.PttTransmit)]
    [InlineData(AudioSessionOwner.PttPlayback)]
    [InlineData(AudioSessionOwner.App)]
    public void LeavingTheChannelAlwaysReturnsOwnershipToTheApp(AudioSessionOwner current)
        => AudioSessionOwnership.OnReleased(current, AudioSessionRelease.ChannelLeft)
            .Should().Be(AudioSessionOwner.App);

    [Fact]
    public void EndingATransmitReleasesOnlyTransmitOwnership()
    {
        // act
        var fromTransmit = AudioSessionOwnership
            .OnReleased(AudioSessionOwner.PttTransmit, AudioSessionRelease.TransmitEnded);
        var fromPlayback = AudioSessionOwnership
            .OnReleased(AudioSessionOwner.PttPlayback, AudioSessionRelease.TransmitEnded);

        // assert
        fromTransmit.Should().Be(AudioSessionOwner.App);
        // Full duplex: a wake playback can still own the session after the transmit ends.
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
    public void OnlyTransmitForbidsConfiguration()
    {
        // Playback keeps today's behaviour: the app may still set category and mode.
        AudioSessionOwnership.MayConfigure(AudioSessionOwner.App).Should().BeTrue();
        AudioSessionOwnership.MayConfigure(AudioSessionOwner.PttPlayback).Should().BeTrue();
        AudioSessionOwnership.MayConfigure(AudioSessionOwner.PttTransmit).Should().BeFalse();
    }
}
