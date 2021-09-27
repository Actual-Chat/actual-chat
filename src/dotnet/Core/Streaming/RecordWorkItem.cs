using System.Runtime.Serialization;
using Stl;

namespace ActualChat.Streaming
{
    [DataContract]
    public record RecordWorkItem<TRecordId, TRecord>(
        [property: DataMember(Order = 0)] TRecord Record,
        [property: DataMember(Order = 1)] RecordReadProgress<TRecordId>? CurrentProgress)
        where TRecordId : struct
        where TRecord : class, IHasId<TRecordId>
    {
        public RecordWorkItem() : this(default!, default) { }
    }
}
