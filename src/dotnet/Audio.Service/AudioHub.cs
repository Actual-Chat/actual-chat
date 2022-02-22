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

    public async Task ProcessAudio(string sessionId, string chatId, double clientStartOffset, IAsyncEnumerable<byte[]> recordingStream)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        var result = await recordingStream.ToListAsync().ConfigureAwait(false);
        using var stream = new FileStream("C:\\Users\\undead\\RiderProjects\\2.opus", FileMode.CreateNew);
        foreach (var byteBlock in result) {
            await stream.WriteAsync(byteBlock, 0, byteBlock.Length).ConfigureAwait(false);
        }

        var audioRecord = new AudioRecord(sessionId, chatId, clientStartOffset);
        await _audioProcessor.ProcessAudio(audioRecord, recordingStream, default)
            .ConfigureAwait(false);
    }
}
