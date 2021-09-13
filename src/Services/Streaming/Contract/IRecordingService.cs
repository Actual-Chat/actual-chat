using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Streaming
{
    public interface IRecordingService<in TRecordingConfiguration> where TRecordingConfiguration : IRecordingConfiguration
    {
        Task UploadRecording(TRecordingConfiguration config, ChannelReader<BlobMessage> source, CancellationToken cancellationToken);
    }
}