using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

/// <summary>
/// Specifies the type of position tracked in a chat.
/// </summary>
public enum ChatPositionKind { Read = 0, View };

/// <summary>
/// Represents a user's position within a chat conversation.
/// </summary>
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
