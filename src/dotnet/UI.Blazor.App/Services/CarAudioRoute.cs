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
/// and whether car projection is active.
/// </summary>
public sealed record CarAudioRoute(AudioEndpoint Input, AudioEndpoint Output)
{
    public static readonly CarAudioRoute Default = new(AudioEndpoint.Default, AudioEndpoint.Default);

    public static CarAudioRoute For(bool isProjectionActive, UserCarAudioSettings settings)
    {
        if (!isProjectionActive)
            return Default;

        var input = settings.Microphone == CarAudioDevice.Car
            ? AudioEndpoint.External
            : AudioEndpoint.Builtin;
        var output = settings.Output == CarAudioDevice.Phone
            ? AudioEndpoint.Builtin
            : AudioEndpoint.External;
        return new CarAudioRoute(input, output);
    }
}
