namespace ActualChat.Invite;

/// <summary>
/// Service for generating and managing invitation links.
/// </summary>
public interface IInvites : IComputeService
{
    [ComputeMethod]
    Task<Invite[]> ListChatInvites(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Invite[]> ListPlaceInvites(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Invite?> GetOrGenerateChatInvite(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Invite?> GetOrGeneratePlaceInvite(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<InviteChatLinkPreview?> GetInviteChatLinkPreview(Session session, string inviteId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Invite> OnGenerate(Invites_Generate command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Invite> OnUse(Invites_Use command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRevoke(Invites_Revoke command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Invites_Generate : ApiCommand<Invite>
{
    [DataMember(Order = 2), Key(2)] public required Invite Invite { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Invites_Use : ApiCommand<Invite>
{
    [DataMember(Order = 2), Key(2)] public required string InviteId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Invites_Revoke : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string InviteId { get; init; }
}
