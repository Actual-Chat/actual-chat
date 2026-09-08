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

    [Theory]
    [InlineData(CarAudioDevice.Auto, AudioEndpoint.Builtin)]
    [InlineData(CarAudioDevice.Phone, AudioEndpoint.Builtin)]
    [InlineData(CarAudioDevice.Car, AudioEndpoint.External)]
    public void ShouldMapMicrophoneUnderProjection(CarAudioDevice setting, AudioEndpoint expected)
    {
        // act
        var route = CarAudioRoute.For(true, new UserCarAudioSettings { Microphone = setting });

        // assert
        route.Input.Should().Be(expected);
    }

    [Theory]
    [InlineData(CarAudioDevice.Auto, AudioEndpoint.External)]
    [InlineData(CarAudioDevice.Car, AudioEndpoint.External)]
    [InlineData(CarAudioDevice.Phone, AudioEndpoint.Builtin)]
    public void ShouldMapOutputUnderProjection(CarAudioDevice setting, AudioEndpoint expected)
    {
        // act
        var route = CarAudioRoute.For(true, new UserCarAudioSettings { Output = setting });

        // assert
        route.Output.Should().Be(expected);
    }

    [Theory]
    [InlineData(CarAudioDevice.Auto)]
    [InlineData(CarAudioDevice.Phone)]
    [InlineData(CarAudioDevice.Car)]
    public void ShouldTakeTheCallLinkOnlyForTheCarMicrophone(CarAudioDevice microphone)
    {
        // act
        var route = CarAudioRoute.For(true, new UserCarAudioSettings { Microphone = microphone });

        // assert
        route.UseCallLink.Should().Be(microphone == CarAudioDevice.Car);
    }

    [Fact]
    public void ShouldCarryPlaybackOverTheCallLink()
    {
        // arrange
        var settings = new UserCarAudioSettings {
            Microphone = CarAudioDevice.Car,
            Output = CarAudioDevice.Phone,
        };

        // act
        var route = CarAudioRoute.For(true, settings);

        // assert
        route.Should().Be(CarAudioRoute.CallLink, because: "a call is two-way on one link, so the output choice yields");
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
