using ActualChat.Audio;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

#pragma warning disable CS0618 // IStreamServer is obsolete; this is the v2.6 compat impl

[Obsolete("2026.05: Use ILiveAudioStreams / ILiveVideoStreams. Kept for v2.7 client compat only.")]
public class StreamServer(IServiceProvider services) : IStreamServer
{
    private ILiveAudioStreams LiveAudioStreams { get; } = services.GetRequiredService<ILiveAudioStreams>();

    public Task<RpcStream<AudioFrame>?> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)
        => LiveAudioStreams.GetStream(Session.Default, streamId, skipTo, cancellationToken);

    public Task<RpcStream<TranscriptDiff>?> GetTranscript(string streamId, CancellationToken cancellationToken)
        => LiveAudioStreams.GetTranscriptStream(Session.Default, streamId, cancellationToken);

    public Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken)
        => LiveAudioStreams.ReportAudioLatency(Session.Default, latency, cancellationToken);

    public Task PushAudio(
        Session session, string chatId, string? repliedChatEntryId,
        double sourceStartOffsetSeconds, int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken)
        => LiveAudioStreams.PushStream(
            session,
            chatId,
            repliedChatEntryId,
            sourceStartOffsetSeconds,
            preSkip,
            frameStream,
            cancellationToken);
}
