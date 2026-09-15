namespace ActualChat.OAuth;

public interface IOAuthGrants : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<OAuthGrant>> List(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<OAuthClientInfo?> GetClient(Session session, string clientId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<string> OnApprove(OAuthGrants_Approve command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRevoke(OAuthGrants_Revoke command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record OAuthGrants_Approve : ApiCommand<string>
{
    [DataMember(Order = 2), Key(2)] public required string ClientId { get; init; }
    [DataMember(Order = 3), Key(3)] public required ApiArray<string> Scopes { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record OAuthGrants_Revoke : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string AuthorizationId { get; init; }
}
