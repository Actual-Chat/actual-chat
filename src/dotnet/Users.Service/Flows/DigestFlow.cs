using ActualChat.Flows;
using ActualChat.Queues;
using MemoryPack;
using TimeZoneConverter;

namespace ActualChat.Users.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class DigestFlow : Flow
{
    public override FlowOptions GetOptions()
        => new() { RemoveDelay = TimeSpan.FromSeconds(1) };

    protected override async Task<FlowTransition> OnStart(CancellationToken cancellationToken)
    {
        var userId = UserId.Parse(Id.Id);
        var delay = await GetDelay(userId, cancellationToken).ConfigureAwait(false);
        return delay is null
            ? JumpToEnd()
            : Wait(nameof(OnTimer)).AddTimerEvent(delay.Value);
    }

    protected async Task<FlowTransition> OnTimer(CancellationToken cancellationToken)
    {
        var userId = UserId.Parse(Id.Id);
        var delay = await GetDelay(userId, cancellationToken).ConfigureAwait(false);
        if (delay is null)
            return JumpToEnd();

        var sendDigestCommand = new EmailsBackend_SendDigest(userId);
        var queues = Host.Services.Queues();
        await queues.Enqueue(sendDigestCommand, cancellationToken).ConfigureAwait(false);
        return Wait(nameof(OnTimer)).AddTimerEvent(delay.Value);
    }

    private async Task<TimeSpan?> GetDelay(UserId userId, CancellationToken cancellationToken)
    {
        var accounts = Host.Services.GetRequiredService<IAccountsBackend>();
        var account = await accounts.Get(userId, cancellationToken).ConfigureAwait(false);
        account.Require(Account.MustExist);
        if (account.TimeZone.IsNullOrEmpty())
            return null;

        if (TZConvert.TryGetTimeZoneInfo(account.TimeZone, out var timeZoneInfo))
            return Host.Clocks.SystemClock.UtcNow.DelayTo(new TimeSpan(9, 0, 0), timeZoneInfo);

        Host.Log.LogWarning("Unable to find time zone info. Time zone: '{TimeZone}'.", account.TimeZone);
        return null;
    }
}
