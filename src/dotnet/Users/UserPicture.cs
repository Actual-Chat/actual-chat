namespace ActualChat.Users;

[DataContract]
public record UserPicture([property: DataMember] string? ContentId, [property: DataMember] string? Picture);
