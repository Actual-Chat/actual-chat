using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR;

namespace ActualChat.Audio;

public class AudioHub : Hub
{
    private readonly AudioSourceStreamer _audioSourceStreamer;
    private readonly AudioStreamer _audioStreamer;
    private readonly SourceAudioRecorder _sourceAudioRecorder;
    private readonly TranscriptStreamer _transcriptStreamer;

    public AudioHub(
        SourceAudioRecorder sourceAudioRecorder,
        AudioStreamer audioStreamer,
        AudioSourceStreamer audioSourceStreamer,
        TranscriptStreamer transcriptStreamer)
    {
        _sourceAudioRecorder = sourceAudioRecorder;
        _audioStreamer = audioStreamer;
        _audioSourceStreamer = audioSourceStreamer;
        _transcriptStreamer = transcriptStreamer;
    }

    public IAsyncEnumerable<AudioStreamPart> GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
        => _audioSourceStreamer.GetAudioStream(streamId, skipTo, cancellationToken);

    public IAsyncEnumerable<BlobPart> GetAudioBlobStream(string streamId, CancellationToken cancellationToken)
        => _audioStreamer.GetAudioBlobStream(streamId, cancellationToken);

    public IAsyncEnumerable<TranscriptUpdate> GetTranscriptStream(
        string streamId,
        CancellationToken cancellationToken)
        => _transcriptStreamer.GetTranscriptStream(streamId, cancellationToken);

    public Task RecordSourceAudio(
            Session session,
            AudioRecord audioRecord,
            IAsyncEnumerable<BlobPart> blobStream)
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        => _sourceAudioRecorder.RecordSourceAudio(session, audioRecord, blobStream, default);
}
