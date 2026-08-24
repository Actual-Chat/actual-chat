using ActualChat.Db.Diagnostics;
using ActualChat.Flows.Infrastructure;
using ActualChat.Queues;
using ActualLab.CommandR.Operations;
using ActualLab.Fusion.EntityFramework.Operations;
using ActualLab.Generators;
using ActualLab.Resilience;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace ActualChat.Db;

[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbEventForwarder<TDbContext>(IServiceProvider services)
    : DbEventProcessor<TDbContext>(services)
    where TDbContext : DbContext
{
    private string ProcessActivityName => field ??= $"{nameof(Process)}@{GetType().GetName()}>";

    private UuidGenerator UuidGenerator { get; } = UlidUuidGenerator.Instance;
    private IQueues Queues { get; } = services.Queues();

    public override async Task Process(OperationEvent operationEvent, CancellationToken cancellationToken)
    {
        var uuid = operationEvent.Uuid;
        var value = operationEvent.Value;
        var delay = (operationEvent.DelayUntil - operationEvent.LoggedAt).Positive();
        var processingDelay = Clocks.SystemClock.Now - operationEvent.DelayUntil;
        var info = delay > TimeSpan.FromSeconds(0.1)
            ? $"{uuid} ({delay.ToShortString()} + {processingDelay.ToShortString()} delay)"
            : $"{uuid} ({processingDelay.ToShortString()} delay)";

        // Forwards everything to Queues
        switch (value) {

        case ICommand command: {
            using var activity = DbInstruments.ActivitySource
                .StartActivity(ProcessActivityName);

            if (value is FlowResumeEvent flowResumeEvent)
                Log.LogInformation("-> {FlowResumeEvent}", flowResumeEvent);
            else
                Log.LogInformation("-> {CommandType}: {Info}", command.GetType().GetName(), info);
            try {
                // If the event was fetched, it has to be executed in the queue,
                // so we need a unique ID instead of the original one.
                await Queues.Enqueue(command, UuidGenerator.Next(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) {
                if (e.IsCancellationOf(cancellationToken) || e.IsServiceProviderDisposedException()) {
                    activity?.SetStatus(ActivityStatusCode.Ok, e.Message);
                    throw;
                }

                activity?.SetStatus(ActivityStatusCode.Error, e.Message);
                if (IsPermanentError(e))
                    throw;

                throw new RetryRequiredException("Queues.Enqueue failed, retry required.", e);
            }
        }
        break;

        case QueuedCommand queuedCommand: {
            ActivityContext senderContext = default;
            IEnumerable<ActivityLink>? links = null;
            var propagationContext = Propagators.DefaultTextMapPropagator
                .Extract(default,
                    queuedCommand.Headers,
                    static (headers, name) => headers.TryGetValue(name, out var value) ? value : []);
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
            catch (Exception e) {
                if (e.IsCancellationOf(cancellationToken) || e.IsServiceProviderDisposedException()) {
                    activity?.SetStatus(ActivityStatusCode.Ok, e.Message);
                    throw;
                }

                activity?.SetStatus(ActivityStatusCode.Error, e.Message);
                if (IsPermanentError(e))
                    throw;

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

    // Private methods

    private static bool IsPermanentError(Exception error)
        // RetryRequiredException is super-transient, i.e. DbLogReader retries it indefinitely - the
        // right default here, since dropping an event is far worse than retrying one. An error no
        // retry can fix - e.g. a resume event of a flow that no longer exists - would spin forever
        // that way, so it's rethrown as-is to let the reader exhaust its retries and discard the event.
        => TransiencyResolvers.PreferTransient.Invoke(error) is Transiency.NonTransient or Transiency.Terminal;
}
