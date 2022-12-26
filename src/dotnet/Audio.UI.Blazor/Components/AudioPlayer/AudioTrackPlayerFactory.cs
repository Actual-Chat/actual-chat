using ActualChat.Media;
using ActualChat.MediaPlayback;

namespace ActualChat.Audio.UI.Blazor.Components;

public sealed class AudioTrackPlayerFactory : ITrackPlayerFactory
{
    private readonly BlazorCircuitContext _circuitContext;
    private readonly IJSRuntime _js;
    private readonly ILogger<AudioTrackPlayer> _audioTrackPlayerLog;
    private ulong _lastCreatedId;

    public AudioTrackPlayerFactory(
        BlazorCircuitContext circuitContext,
        IJSRuntime jsRuntime,
        ILogger<AudioTrackPlayer> audioTrackPlayerLog)
    {
        _circuitContext = circuitContext;
        _js = jsRuntime;
        _audioTrackPlayerLog = audioTrackPlayerLog;
    }

    public TrackPlayer Create(IMediaSource source) => new AudioTrackPlayer(
        Interlocked.Increment(ref _lastCreatedId).Format(),
        source,
        _circuitContext,
        _js,
        _audioTrackPlayerLog);
}
