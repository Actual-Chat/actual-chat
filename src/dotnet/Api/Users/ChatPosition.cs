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
    // A stored 0 is a real position - "read nothing yet" in a chat that has entries - so
    // "no position stored at all" needs a value of its own. Every consumer treats a negative
    // lid the same way it treats 0, except the unread count, which must tell the two apart.
    public static readonly ChatPosition None = new(-1);

    public override string ToString()
        => Origin.IsNullOrEmpty()
            ? EntryLid.Format()
            : $"{EntryLid.Format()} @ '{Origin}'";
}
