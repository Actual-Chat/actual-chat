using ActualChat.Media;
using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR;

namespace ActualChat.Audio;

public class AudioHub : Hub
{
    private readonly IAudioProcessor _audioProcessor;
    private readonly AudioStreamer _audioStreamer;
    private readonly TranscriptStreamer _transcriptStreamer;

    public AudioHub(
        IAudioProcessor audioProcessor,
        AudioStreamer audioStreamer,
        TranscriptStreamer transcriptStreamer)
    {
        _audioProcessor = audioProcessor;
        _audioStreamer = audioStreamer;
        _transcriptStreamer = transcriptStreamer;
    }

    public IAsyncEnumerable<AudioStreamPart> GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
        => _audioStreamer.GetAudioStream(streamId, skipTo, cancellationToken);

    public IAsyncEnumerable<Transcript> GetTranscriptDiffStream(
        string streamId,
        CancellationToken cancellationToken)
        => _transcriptStreamer.GetTranscriptDiffStream(streamId, cancellationToken);

    public async Task ProcessAudio(string sessionId, string chatId, double clientStartOffset, IAsyncEnumerable<byte[]> opusPacketStream)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        var audioRecord = new AudioRecord(sessionId, chatId, clientStartOffset);
        var frameStream = opusPacketStream
            .Select((packet, i) => new AudioFrame {
                Data = packet,
                Offset = TimeSpan.FromMilliseconds(i * 20), // we support only 20-ms packets
            });
        await _audioProcessor.ProcessAudio(audioRecord, frameStream, default)
            .ConfigureAwait(false);
    }
}
