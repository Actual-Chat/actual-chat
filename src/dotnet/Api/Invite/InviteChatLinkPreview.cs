
namespace ActualChat.Invite;

/// <summary>
/// Preview data for an invite link showing the target chat or place.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record InviteChatLinkPreview(
    [property: DataMember, MemoryPackOrder(0)] Chat.Chat? Chat,
    [property: DataMember, MemoryPackOrder(1)] Chat.Place? Place
);
