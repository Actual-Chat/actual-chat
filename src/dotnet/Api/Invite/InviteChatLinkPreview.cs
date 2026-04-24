namespace ActualChat.Invite;

/// <summary>
/// Preview data for an invite link showing the target chat or place.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record InviteChatLinkPreview(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Chat.Chat? Chat,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Chat.Place? Place
);
