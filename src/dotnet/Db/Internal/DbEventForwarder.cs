using ActualChat.Db.Diagnostics;
using ActualChat.Db.Module;
using ActualChat.Queues;
using ActualLab.CommandR.Operations;
using ActualLab.Fusion.EntityFramework.Operations;
using ActualLab.Resilience;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace ActualChat.Db;

public class DbEventForwarder<TDbContext>(IServiceProvider services)
    : DbEventProcessor<TDbContext>(services)
    where TDbContext : DbContext
{
    private readonly string ProcessActivityName =
        $"{nameof(Process)}@{nameof(DbEventForwarder<TDbContext>)}<{typeof(TDbContext).Name}>";
    private IQueues Queues { get; } = services.Queues();

    public override async Task Process(OperationEvent operationEvent, CancellationToken cancellationToken)
    {
        var ulid = operationEvent.Uuid;
        var value = operationEvent.Value;
        var delay = (operationEvent.DelayUntil - operationEvent.LoggedAt).Positive();
        var processingDelay = Clocks.SystemClock.Now - operationEvent.DelayUntil;
        var info = delay > TimeSpan.FromSeconds(0.1)
            ? $"{ulid} ({delay.ToShortString()} + {processingDelay.ToShortString()} delay)"
            : $"{ulid} ({processingDelay.ToShortString()} delay)";

        // Forwards everything to Queues
        switch (value) {
        case ICommand command: {
                using var activity = DbInstruments.ActivitySource
                    .StartActivity(ProcessActivityName, ActivityKind.Internal);

                Log.LogInformation("-> {CommandType}: {Info}", command.GetType().GetName(), info);
                try {
                    await Queues.Enqueue(command, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e.IsCancellationOf(cancellationToken)) {
                    activity?.SetStatus(ActivityStatusCode.Ok, e.Message);
                    throw;
                }
                catch (Exception e) {
                    activity?.SetStatus(ActivityStatusCode.Error, e.Message);
                    throw new RetryRequiredException("Queues.Enqueue failed, retry required.", e);
                }
            }
            break;
        case QueuedCommand queuedCommand: {
                ActivityContext senderContext = default;
                IEnumerable<ActivityLink>? links = null;
                var propagationContext = Propagators.DefaultTextMapPropagator
                    .Extract(default, queuedCommand.Headers, static (headers, name) => headers.TryGetValue(name, out var value) ? value : []);
                if (propagationContext != default) {
                    senderContext = propagationContext.ActivityContext;
                    Baggage.Current = propagationContext.Baggage;
                    links = [new ActivityLink(senderContext)];
                }

                using var activity = DbInstruments.ActivitySource
                    .StartActivity(ProcessActivityName, ActivityKind.Consumer, senderContext, links: links);

                Log.LogInformation("-> {CommandType}: {Info}", queuedCommand.UntypedCommand.GetType().GetName(), info);
                try {
                    await Queues.Enqueue(queuedCommand, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e.IsCancellationOf(cancellationToken)) {
                    activity?.SetStatus(ActivityStatusCode.Ok, e.Message);
                    throw;
                }
                catch (Exception e) {
                    activity?.SetStatus(ActivityStatusCode.Error, e.Message);
                    throw new RetryRequiredException("Queues.Enqueue failed, retry required.", e);
                }
            }
            break;
        default:
            var eventType = value?.GetType().GetName() ?? "null";
            Log.LogError("Unsupported event {EventType}: {Info}", eventType, info);
            break;
        }
    }
}
