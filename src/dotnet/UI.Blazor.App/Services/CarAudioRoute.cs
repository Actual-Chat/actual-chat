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
/// and whether car projection is active. A non-null <see cref="Link"/> means both directions
/// ride the Bluetooth hands-free link - as a phone call, or as a voice-assistant session.
/// </summary>
public sealed record CarAudioRoute(AudioEndpoint Input, AudioEndpoint Output, CarLink? Link = null)
{
    public static readonly CarAudioRoute Default = new(AudioEndpoint.Default, AudioEndpoint.Default);
    public static readonly CarAudioRoute CallLink = new(AudioEndpoint.External, AudioEndpoint.External, CarLink.Call);
    public static readonly CarAudioRoute AssistantLink
        = new(AudioEndpoint.External, AudioEndpoint.External, CarLink.Assistant);

    public bool UseHandsFreeLink => Link != null;
    public bool UseCallLink => Link == CarLink.Call;
    public bool UseAssistantLink => Link == CarLink.Assistant;

    public static CarAudioRoute For(bool isProjectionActive, UserCarAudioSettings settings)
    {
        if (!isProjectionActive)
            return Default;

        return settings.GetCarAudioMode() switch {
            CarAudioMode.Car => CallLink,
            CarAudioMode.CarAssistant => AssistantLink,
            CarAudioMode.Phone => new CarAudioRoute(AudioEndpoint.Builtin, AudioEndpoint.Builtin),
            _ => new CarAudioRoute(AudioEndpoint.Builtin, AudioEndpoint.External),
        };
    }
}

/// <summary>
/// The choices the car audio page offers, projected from the axes of
/// <see cref="UserCarAudioSettings"/>. Car is the zero-default: the car handles both directions.
/// </summary>
public enum CarAudioMode
{
    Car = 0,
    CarSpeakers = 1,
    Phone = 2,
    CarAssistant = 3,
}

public static class CarAudioModeExt
{
    public static CarAudioMode GetCarAudioMode(this UserCarAudioSettings settings)
        => settings.Microphone != CarAudioDevice.Phone
            ? settings.Link == CarLink.Assistant ? CarAudioMode.CarAssistant : CarAudioMode.Car
            : settings.Output == CarAudioDevice.Phone ? CarAudioMode.Phone
            : CarAudioMode.CarSpeakers;

    public static UserCarAudioSettings WithCarAudioMode(this UserCarAudioSettings settings, CarAudioMode mode)
        => mode switch {
            CarAudioMode.Car => settings with {
                Microphone = CarAudioDevice.Car, Output = CarAudioDevice.Car, Link = CarLink.Call,
            },
            CarAudioMode.CarAssistant => settings with {
                Microphone = CarAudioDevice.Car, Output = CarAudioDevice.Car, Link = CarLink.Assistant,
            },
            CarAudioMode.CarSpeakers => settings with {
                Microphone = CarAudioDevice.Phone, Output = CarAudioDevice.Car,
            },
            CarAudioMode.Phone => settings with {
                Microphone = CarAudioDevice.Phone, Output = CarAudioDevice.Phone,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
}
