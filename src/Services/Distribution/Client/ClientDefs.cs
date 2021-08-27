using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Distribution.Client
{

    public class AudioStreamingService : IStreamingService<AudioMessage>
    {
        public AudioStreamingService()
        {
        }

        public Task<ChannelReader<AudioMessage>> GetStream(string streamId, CancellationToken cancellationToken)
        {
            
            throw new System.NotImplementedException();
        }
    }
}
