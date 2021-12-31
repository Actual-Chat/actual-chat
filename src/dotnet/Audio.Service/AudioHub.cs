using ActualChat.Media;
using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR;

namespace ActualChat.Audio;

public class AudioHub : Hub
{
    private readonly AudioSourceStreamer _audioSourceStreamer;
    private readonly SourceAudioRecorder _sourceAudioRecorder;
    private readonly TranscriptStreamer _transcriptStreamer;

    public AudioHub(
        SourceAudioRecorder sourceAudioRecorder,
        AudioSourceStreamer audioSourceStreamer,
        TranscriptStreamer transcriptStreamer)
    {
        _sourceAudioRecorder = sourceAudioRecorder;
        _audioSourceStreamer = audioSourceStreamer;
        _transcriptStreamer = transcriptStreamer;
    }

    public IAsyncEnumerable<AudioStreamPart> GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
        => _audioSourceStreamer.GetAudioStream(streamId, skipTo, cancellationToken);

    public IAsyncEnumerable<Transcript> GetTranscriptDiffStream(
        string streamId,
        CancellationToken cancellationToken)
        => _transcriptStreamer.GetTranscriptDiffStream(streamId, cancellationToken);

    public Task RecordSourceAudio(
            Session session,
            AudioRecord audioRecord,
            IAsyncEnumerable<RecordingPart> recordingStream)
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        => _sourceAudioRecorder.RecordSourceAudio(session, audioRecord, recordingStream, default);
}
