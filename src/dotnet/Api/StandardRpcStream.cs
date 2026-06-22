using ActualLab.Rpc;

namespace ActualChat;

/// <summary>
/// Creates RPC streams with media-specific flow-control policies.
/// </summary>
public static class StandardRpcStream
{
    public static RpcStream<T> NewAudioRecording<T>(IAsyncEnumerable<T> source)
        => new(source) {
            AckPeriod = Constants.Audio.RecordingRpcStreamAckPeriod,
        };

    public static RpcStream<T> NewAudioDelivery<T>(IAsyncEnumerable<T> source, bool allowReconnect = true)
        => new(source) {
            AllowReconnect = allowReconnect,
            AckPeriod = Constants.Audio.DeliveryRpcStreamAckPeriod,
        };

    public static RpcStream<T> NewTranscriptDelivery<T>(IAsyncEnumerable<T> source, bool allowReconnect = true)
        => NewAudioDelivery(source, allowReconnect);

    public static RpcStream<T> NewUpload<T>(IAsyncEnumerable<T> source)
        => new(source) {
            AckPeriod = Constants.Uploads.RpcStreamAckPeriod,
            AckAdvance = Constants.Uploads.RpcStreamAckAdvance,
        };
}
