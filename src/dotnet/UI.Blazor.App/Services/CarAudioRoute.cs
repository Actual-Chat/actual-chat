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
        if (settings.Microphone == CarAudioDevice.Car)
            return CallLink;

        var output = settings.Output == CarAudioDevice.Phone
            ? AudioEndpoint.Builtin
            : AudioEndpoint.External;
        return new CarAudioRoute(AudioEndpoint.Builtin, output);
    }
}
