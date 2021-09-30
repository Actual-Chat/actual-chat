#pragma warning disable 618
namespace ActualChat;

public partial struct StreamId
{
    public StreamId(AudioRecordId id, int index)
        : this ($"{id.Value}-{index:D4}")
    { }
}
