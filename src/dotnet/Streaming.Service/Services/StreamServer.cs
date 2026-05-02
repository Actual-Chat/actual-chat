using ActualChat.Audio;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

#pragma warning disable CS0618 // IStreamServer is obsolete; this is the v2.6 compat impl

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
        double clientStartOffset, int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken)
        => LiveAudioStreams.PushStream(
            session,
            chatId,
            repliedChatEntryId,
            clientStartOffset,
            preSkip,
            frameStream,
            cancellationToken);
}
