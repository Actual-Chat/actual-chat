using ActualChat.Audio;
using ActualChat.Video;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Singleton cache for remote video streams fetched via RPC.
/// Wraps StreamStore without StreamIdValidator (accepts any NodeRef).
/// </summary>
public sealed class RemoteVideoStreamCache : IDisposable
{
    public StreamStore<VideoFrame> Store { get; }

    public RemoteVideoStreamCache(IServiceProvider services)
        => Store = new StreamStore<VideoFrame> {
            ExpirationDelay = Constants.Video.StreamExpirationDelay,
            ReplayTailSize = Constants.Video.ServerReplayTailSize,
            Log = services.LogFor($"{GetType().FullName}.Store"),
        };

    public void Dispose()
        => Store.Dispose();
}

/// <summary>
/// Singleton cache for remote audio streams fetched via RPC.
/// Wraps StreamStore without StreamIdValidator (accepts any NodeRef).
/// </summary>
public sealed class RemoteAudioStreamCache : IDisposable
{
    public StreamStore<AudioFrame> Store { get; }

    public RemoteAudioStreamCache(IServiceProvider services)
        => Store = new StreamStore<AudioFrame> {
            ExpirationDelay = services.GetRequiredService<AudioSettings>().StreamExpirationDelay,
            Log = services.LogFor($"{GetType().FullName}.Store"),
        };

    public void Dispose()
        => Store.Dispose();
}
