namespace ActualChat.Users;

/// <summary>
/// One pass of <see cref="Flows.MilestoneDeliveryFlow"/>: decides, sends and records every milestone of a user
/// that is not settled with every enabled target. Nothing is sent without the user's consent to data collection.
/// </summary>
public sealed class MilestoneDeliverer(IServiceProvider services)
{
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(12),
    ];

    private IReadOnlyList<IMilestoneSender> Senders { get; } = services.GetServices<IMilestoneSender>().ToList();
    private IUserMilestonesBackend MilestonesBackend { get; } = services.GetRequiredService<IUserMilestonesBackend>();
    private IUserAttributionsBackend AttributionsBackend { get; }
        = services.GetRequiredService<IUserAttributionsBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private ICommander Commander { get; } = services.Commander();
    private MomentClockSet Clocks { get; } = services.Clocks();

    // Returns when to run again, if anything waits for a retry
    public async Task<Moment?> Deliver(UserId userId, CancellationToken cancellationToken)
    {
        var milestoneSenders = Senders.Where(s => s.IsEnabled).ToList();
        if (milestoneSenders.Count == 0)
            return null;

        var milestones = await MilestonesBackend.List(userId, cancellationToken).ConfigureAwait(false);
        if (milestones.Count == 0)
            return null;

        var deliveries = (await MilestonesBackend
            .ListDeliveries(userId, cancellationToken)
            .ConfigureAwait(false)
            ).ToDictionary(d => (d.MilestoneId, d.Target));
        var attributions = await AttributionsBackend.List(userId, cancellationToken).ConfigureAwait(false);
        var clients = await MilestonesBackend.ListAnalyticsClients(userId, cancellationToken).ConfigureAwait(false);
        var appSettings = await ServerKvasBackend.ForUser(userId).UserAppSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var isConsented = appSettings.IsDataCollectionEnabled == true;

        var now = Clocks.SystemClock.Now;
        Moment? nextRunAt = null;
        var changes = new List<MilestoneDelivery>();
        foreach (var milestoneSender in milestoneSenders)
        foreach (var milestone in milestones) {
            var delivery = deliveries.GetValueOrDefault((milestone.Id, milestoneSender.Target));
            if (delivery is not null && delivery.State.IsFinal())
                continue;

            if (delivery?.NextAttemptAt is { } nextAttemptAt && nextAttemptAt > now) {
                nextRunAt = Min(nextRunAt, nextAttemptAt);
                continue;
            }

            var newDelivery = delivery ?? new MilestoneDelivery(milestone.Id, milestoneSender.Target, default, now);
            newDelivery = newDelivery with { UpdatedAt = now, AttributionId = milestone.AttributionId };
            if (!isConsented) {
                // Turning consent on resumes the flow, so nothing is scheduled here
                if (delivery?.State != MilestoneDeliveryState.AwaitingConsent)
                    changes.Add(newDelivery with {
                        State = MilestoneDeliveryState.AwaitingConsent,
                        NextAttemptAt = null,
                        Note = "",
                    });
                continue;
            }

            var attribution = attributions.FirstOrDefault(a => a.Id == milestone.AttributionId);
            var context = new MilestoneSendContext(milestone, attribution, clients, now);
            var check = milestoneSender.Check(context);
            if (check.Kind == MilestoneCheckKind.Skip) {
                changes.Add(newDelivery with { State = MilestoneDeliveryState.Skipped, Note = check.Note });
                continue;
            }
            if (check.Kind == MilestoneCheckKind.Wait) {
                nextRunAt = Min(nextRunAt, check.RetryAt!.Value);
                changes.Add(newDelivery with {
                    State = MilestoneDeliveryState.Pending,
                    NextAttemptAt = check.RetryAt,
                    Note = check.Note,
                });
                continue;
            }

            var outcome = await milestoneSender.Send(context, cancellationToken).ConfigureAwait(false);
            var attempts = newDelivery.Attempts + 1;
            var retryAt = attempts <= RetryDelays.Length ? now + RetryDelays[attempts - 1] : (Moment?)null;
            newDelivery = outcome.Result switch {
                MilestoneSendResult.Sent => newDelivery with { State = MilestoneDeliveryState.Sent, Note = "" },
                MilestoneSendResult.Retry when retryAt is not null => newDelivery with {
                    State = MilestoneDeliveryState.Pending,
                    NextAttemptAt = retryAt,
                },
                _ => newDelivery with { State = MilestoneDeliveryState.Failed, NextAttemptAt = null },
            };
            newDelivery = newDelivery with { Attempts = attempts, Note = outcome.Note };
            if (newDelivery.State == MilestoneDeliveryState.Pending)
                nextRunAt = Min(nextRunAt, newDelivery.NextAttemptAt!.Value);
            changes.Add(newDelivery);
        }

        if (changes.Count > 0) {
            var setDeliveriesCmd = new UserMilestonesBackend_SetDeliveries(userId, changes.ToApiArray());
            await Commander.Call(setDeliveriesCmd, cancellationToken).ConfigureAwait(false);
        }
        return nextRunAt;
    }

    // Private methods

    private static Moment Min(Moment? first, Moment second)
        => first is { } vFirst ? Moment.Min(vFirst, second) : second;
}
