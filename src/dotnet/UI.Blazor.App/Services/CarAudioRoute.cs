using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

// Default means "impose nothing" - the platform keeps its own device priority.
public enum AudioEndpoint
{
    Default = 0,
    Builtin = 1,
    External = 2,
}

/// <summary>
/// The audio route in effect right now, derived from <see cref="UserCarAudioSettings"/>
/// and whether car projection is active. <see cref="UseCallLink"/> means both directions
/// ride the Bluetooth hands-free link, which the car treats as a phone call.
/// </summary>
public sealed record CarAudioRoute(AudioEndpoint Input, AudioEndpoint Output, bool UseCallLink = false)
{
    public static readonly CarAudioRoute Default = new(AudioEndpoint.Default, AudioEndpoint.Default);
    public static readonly CarAudioRoute CallLink = new(AudioEndpoint.External, AudioEndpoint.External, true);

    public static CarAudioRoute For(bool isProjectionActive, UserCarAudioSettings settings)
    {
        if (!isProjectionActive)
            return Default;

        return settings.GetCarAudioMode() switch {
            CarAudioMode.Car => CallLink,
            CarAudioMode.Phone => new CarAudioRoute(AudioEndpoint.Builtin, AudioEndpoint.Builtin),
            _ => new CarAudioRoute(AudioEndpoint.Builtin, AudioEndpoint.External),
        };
    }
}

/// <summary>
/// The three choices the car audio page offers, projected from the two axes of
/// <see cref="UserCarAudioSettings"/>. Car is the zero-default: the car handles both directions.
/// </summary>
public enum CarAudioMode
{
    Car = 0,
    CarSpeakers = 1,
    Phone = 2,
}

public static class CarAudioModeExt
{
    public static CarAudioMode GetCarAudioMode(this UserCarAudioSettings settings)
        => settings.Microphone != CarAudioDevice.Phone ? CarAudioMode.Car
            : settings.Output == CarAudioDevice.Phone ? CarAudioMode.Phone
            : CarAudioMode.CarSpeakers;

    public static UserCarAudioSettings WithCarAudioMode(this UserCarAudioSettings settings, CarAudioMode mode)
        => mode switch {
            CarAudioMode.Car => settings with { Microphone = CarAudioDevice.Car, Output = CarAudioDevice.Car },
            CarAudioMode.CarSpeakers => settings with { Microphone = CarAudioDevice.Phone, Output = CarAudioDevice.Car },
            CarAudioMode.Phone => settings with { Microphone = CarAudioDevice.Phone, Output = CarAudioDevice.Phone },
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
}
