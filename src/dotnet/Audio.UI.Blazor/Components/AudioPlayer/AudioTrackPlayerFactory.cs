using ActualChat.Media;
using ActualChat.MediaPlayback;

namespace ActualChat.Audio.UI.Blazor.Components;

public sealed class AudioTrackPlayerFactory : ITrackPlayerFactory
{
    private IServiceProvider Services { get; }
    private ulong _lastCreatedId;

    public AudioTrackPlayerFactory(
        IServiceProvider services)
        => Services = services;

    public TrackPlayer Create(IMediaSource source) => new AudioTrackPlayer(
        Interlocked.Increment(ref _lastCreatedId).Format(),
        source,
        Services.GetRequiredService<BlazorCircuitContext>(),
        Services.GetRequiredService<IJSRuntime>(),
        Services.GetRequiredService<ILogger<AudioTrackPlayer>>());
}
