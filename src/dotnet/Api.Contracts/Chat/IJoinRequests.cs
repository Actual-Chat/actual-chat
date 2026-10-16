namespace ActualChat.Chat;

/// <summary>
/// Requests to join private chats and Places: the landing page of a plain link,
/// and the queue owners and moderators decide.
/// </summary>
public interface IJoinRequests : IComputeService
{
    // Null when the target doesn't exist or its JoinRequestAccess hides it from the caller
    [ComputeMethod]
    Task<JoinTarget?> GetTarget(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<JoinRequest>> ListPending(Session session, ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<JoinRequest> OnSubmit(JoinRequests_Submit command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnWithdraw(JoinRequests_Withdraw command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnApprove(JoinRequests_Approve command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnReject(JoinRequests_Reject command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record JoinRequests_Submit : ApiCommand<JoinRequest>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record JoinRequests_Withdraw : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record JoinRequests_Approve : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required JoinRequestId RequestId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record JoinRequests_Reject : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required JoinRequestId RequestId { get; init; }
}
