using ActualChat.UI.Blazor.App.Services;
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
    [InlineData(CarAudioMode.Car, AudioEndpoint.External, AudioEndpoint.External, true)]
    [InlineData(CarAudioMode.CarSpeakers, AudioEndpoint.Builtin, AudioEndpoint.External, false)]
    [InlineData(CarAudioMode.Phone, AudioEndpoint.Builtin, AudioEndpoint.Builtin, false)]
    public void ShouldMapModeUnderProjection(
        CarAudioMode mode, AudioEndpoint input, AudioEndpoint output, bool useCallLink)
    {
        // act
        var route = CarAudioRoute.For(true, new UserCarAudioSettings().WithCarAudioMode(mode));

        // assert
        route.Should().Be(new CarAudioRoute(input, output, useCallLink));
    }

    [Theory]
    [InlineData(CarAudioMode.Car)]
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

    [Fact]
    public void ShouldNeverTakeTheCallLinkWithoutProjection()
    {
        // act
        var route = CarAudioRoute.For(false, new UserCarAudioSettings { Microphone = CarAudioDevice.Car });

        // assert
        route.UseCallLink.Should().BeFalse();
    }
}
