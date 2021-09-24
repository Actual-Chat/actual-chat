using System.Runtime.Serialization;
using Stl;

namespace ActualChat.Streaming
{
    [DataContract]
    public record RecordReadProgress<TRecordId>(
        [property: DataMember(Order = 0)] TRecordId Id,
        [property: DataMember(Order = 1)] int StreamNumber,
        [property: DataMember(Order = 2)] StreamId StreamId,
        [property: DataMember(Order = 3)] int StreamStartedWithBlobPartNumber,
        [property: DataMember(Order = 4)] int CurrentBlobPartNumber) : IHasId<TRecordId> where TRecordId : struct
    {
        public RecordReadProgress() : this(default, 0, StreamId.None, 0, 0) 
        { }
    }
}
