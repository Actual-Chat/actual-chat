using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Stl.Fusion.Authentication;

namespace ActualChat.Streaming
{
    public interface IRecordingService<in TRecordingConfiguration> where TRecordingConfiguration : IRecordingConfiguration
    {
        Task UploadRecording(Session session, string chatId, TRecordingConfiguration config, ChannelReader<BlobMessage> source, CancellationToken cancellationToken);
    }
}