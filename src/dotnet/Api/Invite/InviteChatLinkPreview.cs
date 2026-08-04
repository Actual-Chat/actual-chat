namespace ActualChat.Invite;

/// <summary>
/// Preview data for an invite link showing the target chat or place.
/// </summary>
[DataContract, MessagePackObject]
public partial record InviteChatLinkPreview(
    [property: DataMember, Key(0)] Chat.Chat? Chat,
    [property: DataMember, Key(1)] Chat.Place? Place
);
