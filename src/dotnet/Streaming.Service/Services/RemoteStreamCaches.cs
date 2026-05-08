using ActualChat.Audio;
using ActualChat.Video;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Singleton cache for remote video streams fetched via RPC. Validates
/// that StreamIds are not for the local node (those go through the
/// backend's StreamStore, never this cache); dedupes concurrent fetches
/// for the same StreamId via EnsureFetched.
/// </summary>
public sealed class RemoteVideoStreamCache : IDisposable
{
    private readonly ConcurrentDictionary<StreamId, Task<bool>> _inflight = new();

    public StreamStore<VideoFrame> Store { get; }

    public RemoteVideoStreamCache(IServiceProvider services)
    {
        var thisNodeRef = services.MeshWatcher().ThisNode.Ref;
        Store = new StreamStore<VideoFrame> {
            StreamIdValidator = streamId => {
                if (streamId.NodeRef == thisNodeRef)
                    throw new ArgumentOutOfRangeException(nameof(streamId),
                        $"Local-node StreamId {streamId} reached the remote cache; "
                        + "use the backend StreamStore for local streams.");
            },
            ExpirationDelay = Constants.Video.StreamExpirationDelay,
            ReplayTailSize = Constants.Video.ServerReplayTailSize,
            Log = services.LogFor($"{GetType().FullName}.Store"),
        };
    }

    public Task<bool> EnsureFetched(StreamId streamId, Func<StreamId, Task<bool>> fetch)
        => StreamCacheFetchDeduper.Run(_inflight, streamId, fetch);

    public void Dispose()
        => Store.Dispose();
}

/// <summary>
/// Singleton cache for remote audio streams fetched via RPC. Validates
/// that StreamIds are not for the local node (those go through the
/// backend's StreamStore, never this cache); dedupes concurrent fetches
/// for the same StreamId via EnsureFetched.
/// </summary>
public sealed class RemoteAudioStreamCache : IDisposable
{
    private readonly ConcurrentDictionary<StreamId, Task<bool>> _inflight = new();

    public StreamStore<AudioFrame> Store { get; }

    public RemoteAudioStreamCache(IServiceProvider services)
    {
        var thisNodeRef = services.MeshWatcher().ThisNode.Ref;
        Store = new StreamStore<AudioFrame> {
            StreamIdValidator = streamId => {
                if (streamId.NodeRef == thisNodeRef)
                    throw new ArgumentOutOfRangeException(nameof(streamId),
                        $"Local-node StreamId {streamId} reached the remote cache; "
                        + "use the backend StreamStore for local streams.");
            },
            ExpirationDelay = services.GetRequiredService<AudioSettings>().StreamExpirationDelay,
            Log = services.LogFor($"{GetType().FullName}.Store"),
        };
    }

    public Task<bool> EnsureFetched(StreamId streamId, Func<StreamId, Task<bool>> fetch)
        => StreamCacheFetchDeduper.Run(_inflight, streamId, fetch);

    public void Dispose()
        => Store.Dispose();
}

internal static class StreamCacheFetchDeduper
{
    // Coalesces concurrent fetches for the same StreamId: callers that
    // arrive while a fetch is in flight observe the same Task; the winning
    // caller drives the fetch and entry removal happens in finally so the
    // map never holds a completed task longer than one continuation hop.
    public static async Task<bool> Run(
        ConcurrentDictionary<StreamId, Task<bool>> inflight,
        StreamId streamId,
        Func<StreamId, Task<bool>> fetch)
    {
        var ownTcs = TaskCompletionSourceExt.New<bool>();
        var task = inflight.GetOrAdd(streamId, ownTcs.Task);
        if (task != ownTcs.Task)
            return await task.ConfigureAwait(false);

        try {
            var ok = await fetch(streamId).ConfigureAwait(false);
            ownTcs.TrySetResult(ok);
            return ok;
        }
        catch (Exception e) {
            ownTcs.TrySetException(e);
            throw;
        }
        finally {
            inflight.TryRemove(streamId, out _);
        }
    }
}
