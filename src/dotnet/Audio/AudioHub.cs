using ActualChat.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Stl.Fusion.Authentication;

namespace ActualChat.Audio;

[Authorize]
public class AudioHub : Hub
{
    private readonly SourceAudioRecorder _sourceAudioRecorder;
    private readonly AudioStreamer _audioStreamer;
    private readonly TranscriptStreamer _transcriptStreamer;

    public AudioHub(
        SourceAudioRecorder sourceAudioRecorder,
        AudioStreamer audioStreamer,
        TranscriptStreamer transcriptStreamer)
    {
        _sourceAudioRecorder = sourceAudioRecorder;
        _audioStreamer = audioStreamer;
        _transcriptStreamer = transcriptStreamer;
    }

    public Task RecordSourceAudio(
        Session session,
        AudioRecord audioRecord,
        ChannelReader<BlobPart> content,
        CancellationToken cancellationToke)
        => _sourceAudioRecorder.RecordSourceAudio(session, audioRecord, content, cancellationToke);

    public Task<ChannelReader<BlobPart>> GetAudioStream(string streamId, CancellationToken cancellationToken)
        => _audioStreamer.GetAudioStream(streamId, cancellationToken);

    public Task<ChannelReader<TranscriptPart>> GetTranscriptStream(string streamId, CancellationToken cancellationToken)
        => _transcriptStreamer.GetTranscriptStream(streamId, cancellationToken);
}
