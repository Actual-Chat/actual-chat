using ActualChat.Jobs;

namespace ActualChat.Chat.Jobs;

[DataContract]
public record OnInviteToChatJob(
    [property: DataMember(Order = 0)] string ChatId,
    [property: DataMember(Order = 1)] string UserId) : IJob;
