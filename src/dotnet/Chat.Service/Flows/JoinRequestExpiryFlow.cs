using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Closes join requests nobody decided within their lifespan. Expiry is silent for the requester;
/// closing still raises the change event, so moderators' notifications are cleared.
/// </summary>
[Flow(DelayQuanta = 60)]
[DataContract, MessagePackObject(true)]
public sealed partial class JoinRequestExpiryFlow : PeriodicFlow, IMasterFlow
{
    private const int BatchSize = 50;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private IJoinRequestsBackend JoinRequestsBackend => field ??= Services.GetRequiredService<IJoinRequestsBackend>();
    private ICommander Commander => field ??= Services.Commander();

    protected override ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
        => ValueTask.FromResult(FlowReadiness.Ready);

    protected override async ValueTask<Moment> Run(CancellationToken cancellationToken)
    {
        var requests = await JoinRequestsBackend
            .ListExpired(default, BatchSize, cancellationToken)
            .ConfigureAwait(false);
        if (requests.Count == 0)
            return Hub.SystemNow + CheckInterval;

        var results = await requests.Select(Expire).CollectResults(cancellationToken).ConfigureAwait(false);
        var failedCount = results.Count(x => x.HasError);
        if (failedCount > 0) {
            Console.LogError($"Failed to expire {failedCount} of {requests.Count} join requests");
            return Hub.SystemNow + CheckInterval;
        }

        return requests.Count == BatchSize
            ? Hub.SystemNow
            : Hub.SystemNow + CheckInterval;

        Task<JoinRequest> Expire(JoinRequest request) {
            var expireCmd = new JoinRequestsBackend_ChangeStatus(
                request.ChatId, request.Id, JoinRequestStatus.Expired);
            return Commander
                .Call(expireCmd, isOutermost: true, cancellationToken)
                .WithErrorLog(cancellationToken, Runtime.Log, "Failed to expire join request #{Id}", request.Id);
        }
    }
}
