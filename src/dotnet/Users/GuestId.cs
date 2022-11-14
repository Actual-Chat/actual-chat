namespace ActualChat.Users;

[DataContract]
public sealed record GuestId(
    [property: DataMember(Order = 0)] UserId UserId
    );
