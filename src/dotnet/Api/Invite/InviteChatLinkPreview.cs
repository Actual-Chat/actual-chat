using MemoryPack;

namespace ActualChat.Invite;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record InviteChatLinkPreview(
    [property: DataMember, MemoryPackOrder(0)] Chat.Chat? Chat,
    [property: DataMember, MemoryPackOrder(1)] Chat.Place? Place
);
