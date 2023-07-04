using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

public enum ChatPositionKind { Read = 0, View };

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatPosition(
    [property: DataMember, MemoryPackOrder(0)] long EntryLid = 0,
    [property: DataMember, MemoryPackOrder(1)] string Origin = ""
) : IHasOrigin
{
    public override string ToString()
        => Origin.IsNullOrEmpty()
            ? EntryLid.Format()
            : $"{EntryLid.Format()} @ '{Origin}'";
}
