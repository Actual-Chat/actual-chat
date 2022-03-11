using ActualChat.Media;
using ActualChat.MediaPlayback;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Audio.UI.Blazor.Components;

public class AudioTrackPlayerFactory : ITrackPlayerFactory
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly BlazorCircuitContext _circuitContext;
    private readonly IJSRuntime _js;
    private readonly ILoggerFactory _loggerFactory;
    private ulong _lastCreatedId;

    public AudioTrackPlayerFactory(
        BlazorCircuitContext circuitContext,
        IJSRuntime jsRuntime,
        ILoggerFactory loggerFactory,
        IHostApplicationLifetime lifetime
    )
    {
        _circuitContext = circuitContext;
        _js = jsRuntime;
        _loggerFactory = loggerFactory;
        _lifetime = lifetime;
    }

    public TrackPlayer Create(IMediaSource source) => new AudioTrackPlayer(
        Interlocked.Increment(ref _lastCreatedId).ToString(CultureInfo.InvariantCulture),
        source,
        _lifetime,
        _circuitContext,
        _js,
        _loggerFactory.CreateLogger<AudioTrackPlayer>()
    );
}
