using ActualChat.Blobs;
using ActualChat.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Stl.Fusion.Authentication;

namespace ActualChat.Audio.Client;

public class AudioClient : HubClientBase,
    ISourceAudioRecorder,
    IAudioStreamer,
    ITranscriptStreamer
{
    public AudioClient(IServiceProvider services) : base(services, "api/hub/audio") { }

    public async Task RecordSourceAudio(Session session, AudioRecord audioRecord, ChannelReader<BlobPart> content, CancellationToken cancellationToken)
    {
        await EnsureConnected(cancellationToken);
        await HubConnection.SendCoreAsync("RecordSourceAudio", new object[] {session, audioRecord, content}, cancellationToken);
    }

    public async Task<ChannelReader<BlobPart>> GetAudioStream(StreamId streamId, CancellationToken cancellationToken)
    {
        await EnsureConnected(cancellationToken);
        return await HubConnection.StreamAsChannelCoreAsync<BlobPart>("GetAudioStream", new object[] { streamId }, cancellationToken);
    }

    public async Task<ChannelReader<TranscriptPart>> GetTranscriptStream(StreamId streamId, CancellationToken cancellationToken)
    {
        await EnsureConnected(cancellationToken);
        return await HubConnection.StreamAsChannelCoreAsync<TranscriptPart>("GetTranscriptStream", new object[] { streamId }, cancellationToken);
    }
}
