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
public sealed partial record Invites_Generate(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] Invite Invite
) : ISessionCommand<Invite>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Invites_Use(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] string InviteId
) : ISessionCommand<Invite>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Invites_Revoke(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] string InviteId
) : ISessionCommand<Unit>, IApiCommand;
