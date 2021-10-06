using ActualChat.Blobs;
using ActualChat.SignalR.Client;
using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR.Client;
using Stl.Fusion.Authentication;

namespace ActualChat.Audio.Client;

public class AudioClient : HubClientBase,
    ISourceAudioRecorder,
    IAudioStreamer,
    IAudioSourceStreamer,
    ITranscriptStreamer
{
    public AudioClient(IServiceProvider services) : base(services, "api/hub/audio") { }

    public async Task RecordSourceAudio(Session session, AudioRecord audioRecord, ChannelReader<BlobPart> content, CancellationToken cancellationToken)
    {
        await EnsureConnected(cancellationToken).ConfigureAwait(false);
        await HubConnection.SendAsync("RecordSourceAudio",
            session, audioRecord, content,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChannelReader<BlobPart>> GetAudioStream(StreamId streamId, CancellationToken cancellationToken)
    {
        await EnsureConnected(cancellationToken);
        return await HubConnection.StreamAsChannelAsync<BlobPart>("GetAudioStream",
            streamId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AudioSource> GetAudioSource(StreamId streamId, CancellationToken cancellationToken)
    {
        await EnsureConnected(cancellationToken).ConfigureAwait(false);
        var audioSourcePartsReader =  await HubConnection.StreamAsChannelAsync<AudioSourcePart>("GetAudioStream",
            streamId,
            cancellationToken);

        return await AudioSourceHelper.ConvertToAudioSource(audioSourcePartsReader, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ChannelReader<TranscriptUpdate>> GetTranscriptStream(StreamId streamId, CancellationToken cancellationToken)
    {
        await EnsureConnected(cancellationToken).ConfigureAwait(false);
        return await HubConnection.StreamAsChannelAsync<TranscriptUpdate>("GetTranscriptStream",
            streamId,
            cancellationToken).ConfigureAwait(false);
    }
}
