using ActualChat.Flows;
using ActualChat.Queues;
using MemoryPack;
using TimeZoneConverter;

namespace ActualChat.Users.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class DigestFlow : Flow
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Moment? LastDigestAt { get; private set; }

    public override FlowOptions GetOptions()
        => new() { RemoveDelay = TimeSpan.FromSeconds(1) };

    protected override Task<FlowTransition> OnStart(CancellationToken cancellationToken)
        => GetDefaultTransition(cancellationToken);

    protected async Task<FlowTransition> OnCheck(CancellationToken cancellationToken)
    {
        Event.Require<FlowTimerEvent>();
        var delayOpt = await GetNextDigestDelay(cancellationToken).ConfigureAwait(false);
        if (delayOpt is not { } delay)
            return GotoToEnd();
        if (delay > TimeSpan.Zero)
            Wait(nameof(OnCheck)).AddTimerEvent(delay);

        var userId = UserId.Parse(Id.Id);
        var sendDigestCommand = new EmailsBackend_SendDigest(userId);
        var queues = Host.Services.Queues();
        await queues.Enqueue(sendDigestCommand, cancellationToken).ConfigureAwait(false);
        LastDigestAt = Clocks.SystemClock.Now;
        return await GetDefaultTransition(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<FlowTransition> GetDefaultTransition(CancellationToken cancellationToken)
    {
        var delayOpt = await GetNextDigestDelay(cancellationToken).ConfigureAwait(false);
        return delayOpt is { } delay
            ? Wait(nameof(OnCheck)).AddTimerEvent(delay + TimeSpan.FromSeconds(5))
            : GotoToEnd();
    }

    private async Task<TimeSpan?> GetNextDigestDelay(CancellationToken cancellationToken)
    {
        var userId = UserId.Parse(Id.Id);
        var accounts = Host.Services.GetRequiredService<IAccountsBackend>();
        var account = await accounts.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account?.IsGuestOrNone != false) {
            Log.LogWarning("`{Id}`: No account", Id);
            return null;
        }

        if (account.TimeZone.IsNullOrEmpty()) {
            Log.LogInformation("`{Id}`: Account has no time zone", Id);
            return null;
        }

        if (!TZConvert.TryGetTimeZoneInfo(account.TimeZone, out var timeZoneInfo)) {
            Log.LogWarning("`{Id}`: Can't find TimeZoneInfo for time zone: {TimeZone}", Id, account.TimeZone);
            return null;
        }

        var serverKvasBackend = Host.Services.GetRequiredService<IServerKvasBackend>();
        var kvas = serverKvasBackend.GetUserClient(userId);
        var userEmailsSettings = await kvas.GetUserEmailsSettings(cancellationToken).ConfigureAwait(false);
        if (!userEmailsSettings.IsDigestEnabled) {
            Log.LogInformation("`{Id}`: Digest is disabled for this account", Id);
            return null;
        }

        var now = Clocks.SystemClock.Now;
        LastDigestAt ??= now;
        var delay = timeZoneInfo.NextTimeOfDay(userEmailsSettings.DigestTime, LastDigestAt.Value) - now;
        return delay.Positive();
    }
}
