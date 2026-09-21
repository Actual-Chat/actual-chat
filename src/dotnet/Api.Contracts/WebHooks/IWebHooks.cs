namespace ActualChat.WebHooks;

public interface IWebHooks : IComputeService
{
    [ComputeMethod]
    Task<WebHook?> Get(Session session, WebHookId id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<WebHook>> List(
        Session session, WebHookScope scope, string scopeId, CancellationToken cancellationToken);
    // Hooks the caller created in any scope, including ones they can no longer manage
    [ComputeMethod]
    Task<ApiArray<WebHook>> ListMine(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<WebHookDelivery>> ListDeliveries(Session session, WebHookId id, CancellationToken cancellationToken);

    [CommandHandler]
    Task<WebHookChangeResult> OnChange(WebHooks_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<string> OnRotateSecret(WebHooks_RotateSecret command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<WebHookTestResult> OnTest(WebHooks_Test command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRedeliver(WebHooks_Redeliver command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooks_Change : ApiCommand<WebHookChangeResult>
{
    [DataMember(Order = 2), Key(2)] public required WebHookScope Scope { get; init; }
    [DataMember(Order = 3), Key(3)] public required string ScopeId { get; init; }
    [DataMember(Order = 4), Key(4)] public WebHookId? Id { get; init; }
    [DataMember(Order = 5), Key(5)] public long? ExpectedVersion { get; init; }
    [DataMember(Order = 6), Key(6)] public required Change<WebHookDiff> Change { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooks_RotateSecret : ApiCommand<string>
{
    [DataMember(Order = 2), Key(2)] public required WebHookId Id { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooks_Test : ApiCommand<WebHookTestResult>
{
    [DataMember(Order = 2), Key(2)] public required WebHookId Id { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooks_Redeliver : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required WebHookId Id { get; init; }
    [DataMember(Order = 3), Key(3)] public required string DeliveryId { get; init; }
}
