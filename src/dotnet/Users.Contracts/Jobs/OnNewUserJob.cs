using ActualChat.Jobs;

namespace ActualChat.Users.Jobs;

[DataContract]
public record OnNewUserJob(
    [property: DataMember(Order = 0)]
    string UserId) : IJob;
