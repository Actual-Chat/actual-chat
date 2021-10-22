using ActualChat.Blobs;
using ActualChat.Transcription;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ActualChat.Audio;

[Authorize]
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

    public Task<ChannelReader<AudioSourcePart>> GetAudioSourceParts(
        StreamId streamId,
        TimeSpan offset,
        CancellationToken cancellationToken)
        => _audioSourceStreamer.GetAudioSourceParts(streamId, offset, cancellationToken);

    public Task<ChannelReader<BlobPart>> GetAudioStream(StreamId streamId, CancellationToken cancellationToken)
        => _audioStreamer.GetAudioStream(streamId, cancellationToken);

    public Task<ChannelReader<TranscriptUpdate>> GetTranscriptStream(
        StreamId streamId,
        CancellationToken cancellationToken)
        => _transcriptStreamer.GetTranscriptStream(streamId, cancellationToken);

    public Task RecordSourceAudio(
            Session session,
            AudioRecord audioRecord,
            ChannelReader<BlobPart> content)
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        => _sourceAudioRecorder.RecordSourceAudio(session, audioRecord, content, default);
}
