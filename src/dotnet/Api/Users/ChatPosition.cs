using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Specifies the type of position tracked in a chat.
/// </summary>
public enum ChatPositionKind { Read = 0, View, Heard };

/// <summary>
/// Represents a user's position within a chat conversation.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatPosition(
    [property: DataMember, Key(0)] long EntryLid = 0,
    [property: DataMember, Key(1)] string Origin = ""
) : IHasOrigin
{
    public override string ToString()
        => Origin.IsNullOrEmpty()
            ? EntryLid.Format()
            : $"{EntryLid.Format()} @ '{Origin}'";
}
