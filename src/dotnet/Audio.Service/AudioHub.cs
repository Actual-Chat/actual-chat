using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR;
using Stl.Fusion.Server.Authentication;

namespace ActualChat.Audio;

public class AudioHub : Hub
{
    private IAudioStreamServer AudioStreamServer { get; }
    private ITranscriptStreamServer TranscriptStreamServer { get; }

    private readonly IAudioProcessor _audioProcessor;
    private readonly SessionMiddleware _sessionMiddleware;

    public AudioHub(
        IAudioProcessor audioProcessor,
        IAudioStreamServer audioStreamServer,
        ITranscriptStreamServer transcriptStreamServer,
        SessionMiddleware sessionMiddleware)
    {
        AudioStreamServer = audioStreamServer;
        TranscriptStreamServer = transcriptStreamServer;
        _audioProcessor = audioProcessor;
        _sessionMiddleware = sessionMiddleware;
    }

    public IAsyncEnumerable<byte[]> GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
        => AudioStreamServer.Read(streamId, skipTo, cancellationToken);

    public IAsyncEnumerable<Transcript> GetTranscriptDiffStream(
        string streamId,
        CancellationToken cancellationToken)
        => TranscriptStreamServer.Read(streamId, cancellationToken);

    public async Task ProcessAudio(string sessionId, string chatId, double clientStartOffset, IAsyncEnumerable<byte[]> opusPacketStream)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!

        var httpContext = Context.GetHttpContext()!;
        var cancellationToken = httpContext.RequestAborted;
        var session = _sessionMiddleware.GetSession(httpContext).Require();

        var audioRecord = new AudioRecord(session.Id, chatId, clientStartOffset);
        var frameStream = opusPacketStream
            .Select((packet, i) => new AudioFrame {
                Data = packet,
                Offset = TimeSpan.FromMilliseconds(i * 20), // we support only 20-ms packets
            });
        await _audioProcessor.ProcessAudio(audioRecord, frameStream, cancellationToken)
            .ConfigureAwait(false);
    }
}
