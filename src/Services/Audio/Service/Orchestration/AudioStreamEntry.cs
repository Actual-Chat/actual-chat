using System.Threading.Channels;
using ActualChat.Distribution;
using Stl.Text;

namespace ActualChat.Audio.Orchestration
{
    public readonly struct AudioStreamEntry
    {
        public AudioStreamEntry(Symbol streamId, ChannelReader<AudioRecordMessage> audioStream)
        {
            StreamId = streamId;
            AudioStream = audioStream;
        }

        public Symbol StreamId { get; }
        public ChannelReader<AudioRecordMessage> AudioStream { get; }

    }
    
    public record MetaDataEntry(int Index, double Offset, double Duration);

}