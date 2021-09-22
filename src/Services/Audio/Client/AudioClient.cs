using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Blobs;
using ActualChat.Streaming;
using ActualChat.Streaming.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Stl.Fusion.Authentication;

namespace ActualChat.Audio.Client
{
    public class AudioClient : HubClientBase, IAudioRecorder, IAudioStreamProvider, ITranscriptStreamProvider
    {
        public AudioClient(IServiceProvider services) : base(services, "api/hub/audio") { }

        public async Task Record(Session session, AudioRecord record, ChannelReader<BlobPart> content, CancellationToken cancellationToken)
        {
            await EnsureConnected(cancellationToken);
            await HubConnection.SendCoreAsync("UploadAudioStream", new object[] {session, record, content}, cancellationToken);
        }

        Task<ChannelReader<BlobPart>> IStreamProvider<StreamId, BlobPart>.GetStream(StreamId streamId, CancellationToken cancellationToken)
            => GetAudioStream(streamId, cancellationToken);
        public async Task<ChannelReader<BlobPart>> GetAudioStream(StreamId streamId, CancellationToken cancellationToken)
        {
            await EnsureConnected(cancellationToken);
            return await HubConnection.StreamAsChannelCoreAsync<BlobPart>("GetAudioStream", new object[] { streamId }, cancellationToken);
        }

        Task<ChannelReader<TranscriptPart>> IStreamProvider<StreamId, TranscriptPart>.GetStream(StreamId streamId, CancellationToken cancellationToken)
            => GetTranscriptStream(streamId, cancellationToken);
        public async Task<ChannelReader<TranscriptPart>> GetTranscriptStream(StreamId streamId, CancellationToken cancellationToken)
        {
            await EnsureConnected(cancellationToken);
            return await HubConnection.StreamAsChannelCoreAsync<TranscriptPart>("GetTranscriptStream", new object[] { streamId }, cancellationToken);
        }
    }
}
