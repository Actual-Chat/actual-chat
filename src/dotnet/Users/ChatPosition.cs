using ActualChat.Kvas;

namespace ActualChat.Users;

public enum ChatPositionKind { Read = 0, View };

[DataContract]
public sealed record ChatPosition(
    [property: DataMember] long EntryLid = 0,
    [property: DataMember] string Origin = ""
) : IHasOrigin
{
    public override string ToString()
        => Origin.IsNullOrEmpty()
            ? EntryLid.Format()
            : $"{EntryLid.Format()} @ '{Origin}'";
}
