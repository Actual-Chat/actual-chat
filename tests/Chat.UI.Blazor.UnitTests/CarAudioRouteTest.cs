using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CarAudioRouteTest
{
    [Fact]
    public void ShouldImposeNothingWithoutProjection()
    {
        // arrange
        var settings = new UserCarAudioSettings { Microphone = CarAudioDevice.Phone };

        // act
        var route = CarAudioRoute.For(false, settings);

        // assert
        route.Should().Be(CarAudioRoute.Default);
    }

    [Fact]
    public void ShouldDefaultToTheCarCall()
    {
        // act
        var route = CarAudioRoute.For(true, new UserCarAudioSettings());

        // assert
        route.Should().Be(CarAudioRoute.CallLink, because: "the zero settings mean the car handles both directions");
    }

    [Theory]
    [InlineData(CarAudioMode.Car, AudioEndpoint.External, AudioEndpoint.External, CarLink.Call)]
    [InlineData(CarAudioMode.CarAssistant, AudioEndpoint.External, AudioEndpoint.External, CarLink.Assistant)]
    [InlineData(CarAudioMode.CarSpeakers, AudioEndpoint.Builtin, AudioEndpoint.External, null)]
    [InlineData(CarAudioMode.Phone, AudioEndpoint.Builtin, AudioEndpoint.Builtin, null)]
    public void ShouldMapModeUnderProjection(
        CarAudioMode mode, AudioEndpoint input, AudioEndpoint output, CarLink? link)
    {
        // act
        var route = CarAudioRoute.For(true, new UserCarAudioSettings().WithCarAudioMode(mode));

        // assert
        route.Should().Be(new CarAudioRoute(input, output, link));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryPlaybackShouldUseCommunicationOnlyForACall(bool isCallActive)
    {
        // act
        var mustUseCommunication = CarAudioRoute.Default.UseCommunicationPlayback(isCallActive);

        // assert
        mustUseCommunication.Should().Be(isCallActive,
            "PTT playback must not move to the earpiece when a concurrent reply releases its recording focus");
    }

    [Theory]
    [InlineData(CarAudioMode.Car, true)]
    [InlineData(CarAudioMode.CarAssistant, true)]
    [InlineData(CarAudioMode.CarSpeakers, false)]
    [InlineData(CarAudioMode.Phone, true)]
    public void ProjectionPlaybackShouldKeepItsExplicitRoute(CarAudioMode mode, bool mustUseCommunication)
    {
        // arrange
        var route = CarAudioRoute.For(true, new UserCarAudioSettings().WithCarAudioMode(mode));

        // act
        var mustUseCommunicationWithoutCall = route.UseCommunicationPlayback(false);
        var mustUseCommunicationWithCall = route.UseCommunicationPlayback(true);

        // assert
        mustUseCommunicationWithoutCall.Should().Be(mustUseCommunication);
        mustUseCommunicationWithCall.Should().Be(mustUseCommunication);
    }

    [Fact]
    public void RecordingCommunicationFocusShouldNotBindPttPlaybackToTheCallRoute()
    {
        // arrange
        var focus = new RecordingAudioFocusUI();

        // act
        var mustUseCommunication = CarAudioRoute.Default.UseCommunicationPlayback(focus.IsCallActive);

        // assert
        focus.IsCommunicationFocus.Should().BeTrue();
        focus.ActiveMode.Should().Be(AudioFocusMode.Recording);
        mustUseCommunication.Should().BeFalse();
    }

    [Fact]
    public void ShouldTellTheLinksApart()
    {
        // assert
        CarAudioRoute.CallLink.UseCallLink.Should().BeTrue();
        CarAudioRoute.CallLink.UseAssistantLink.Should().BeFalse();
        CarAudioRoute.AssistantLink.UseCallLink.Should().BeFalse();
        CarAudioRoute.AssistantLink.UseAssistantLink.Should().BeTrue();
        CarAudioRoute.AssistantLink.UseHandsFreeLink.Should().BeTrue();
        CarAudioRoute.Default.UseHandsFreeLink.Should().BeFalse();
    }

    [Theory]
    [InlineData(CarAudioMode.Car)]
    [InlineData(CarAudioMode.CarAssistant)]
    [InlineData(CarAudioMode.CarSpeakers)]
    [InlineData(CarAudioMode.Phone)]
    public void ShouldRoundTripMode(CarAudioMode mode)
    {
        // act
        var settings = new UserCarAudioSettings().WithCarAudioMode(mode);

        // assert
        settings.GetCarAudioMode().Should().Be(mode);
    }

    [Theory]
    [InlineData(CarAudioDevice.Auto, CarAudioDevice.Auto, CarAudioMode.Car)]
    [InlineData(CarAudioDevice.Car, CarAudioDevice.Phone, CarAudioMode.Car)]
    [InlineData(CarAudioDevice.Phone, CarAudioDevice.Auto, CarAudioMode.CarSpeakers)]
    [InlineData(CarAudioDevice.Phone, CarAudioDevice.Phone, CarAudioMode.Phone)]
    public void ShouldReadStoredAxesAsMode(CarAudioDevice microphone, CarAudioDevice output, CarAudioMode expected)
    {
        // arrange
        var settings = new UserCarAudioSettings { Microphone = microphone, Output = output };

        // assert
        settings.GetCarAudioMode().Should().Be(expected, because: "a phone-only choice needs the phone mic first");
    }

    [Theory]
    [InlineData(CarAudioDevice.Auto, CarAudioMode.CarAssistant)]
    [InlineData(CarAudioDevice.Car, CarAudioMode.CarAssistant)]
    [InlineData(CarAudioDevice.Phone, CarAudioMode.CarSpeakers)]
    public void ShouldReadTheAssistantLinkOnlyWithTheCarMicrophone(CarAudioDevice microphone, CarAudioMode expected)
    {
        // arrange
        var settings = new UserCarAudioSettings { Microphone = microphone, Link = CarLink.Assistant };

        // assert
        settings.GetCarAudioMode().Should().Be(expected, because: "the link only matters when the car records");
    }

    [Fact]
    public void ShouldDropTheAssistantLinkWhenLeavingTheCarMode()
    {
        // arrange
        var settings = new UserCarAudioSettings().WithCarAudioMode(CarAudioMode.CarAssistant);

        // act
        var back = settings.WithCarAudioMode(CarAudioMode.CarSpeakers).WithCarAudioMode(CarAudioMode.Car);

        // assert
        back.Link.Should().Be(CarLink.Call, because: "Car is the plain call link, whatever was chosen before");
    }

    [Fact]
    public void ShouldNeverTakeTheCallLinkWithoutProjection()
    {
        // act
        var route = CarAudioRoute.For(false, new UserCarAudioSettings { Microphone = CarAudioDevice.Car });

        // assert
        route.UseCallLink.Should().BeFalse();
    }

    // Nested types

    private sealed class RecordingAudioFocusUI : AudioFocusUI
    {
        public override AudioFocusMode ActiveMode => AudioFocusMode.Recording;
        public override bool IsCommunicationFocus => true;
    }
}
