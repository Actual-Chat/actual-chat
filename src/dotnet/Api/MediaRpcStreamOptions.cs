using ActualLab.Rpc;

namespace ActualChat;

/// <summary>
/// Creates RPC streams with media-specific flow-control policies.
/// </summary>
public static class MediaRpcStreamOptions
{
    public static RpcStream<T> AudioRecording<T>(IAsyncEnumerable<T> source)
        => new(source) {
            AckPeriod = Constants.Audio.RecordingRpcStreamAckPeriod,
        };

    public static RpcStream<T> AudioDelivery<T>(IAsyncEnumerable<T> source, bool allowReconnect = true)
        => new(source) {
            AllowReconnect = allowReconnect,
            AckPeriod = Constants.Audio.DeliveryRpcStreamAckPeriod,
        };

    public static RpcStream<T> TranscriptDelivery<T>(IAsyncEnumerable<T> source, bool allowReconnect = true)
        => AudioDelivery(source, allowReconnect);
}
