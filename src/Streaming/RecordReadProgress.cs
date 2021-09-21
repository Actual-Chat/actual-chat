using System.Runtime.Serialization;

namespace ActualChat.Streaming
{
    [DataContract]
    public record RecordingProgress(
        [property:DataMember(Order = 0)]AudioRecordId Id,
        [property:DataMember(Order = 1)]int StreamNumber,
        [property:DataMember(Order = 2)]StreamId StreamId,
        [property:DataMember(Order = 3)]int StreamStartedWithBlobPartNumber,
        [property:DataMember(Order = 4)]int CurrentBlobPartNumber)
    {
        public RecordingProgress() : this(AudioRecordId.None, 0, StreamId.None, 0, 0)
        { }
    }
}