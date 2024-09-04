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
        var userId = UserId.Parse(Id.Arguments);

        var serverKvasBackend = Host.Services.GetRequiredService<IServerKvasBackend>();
        var kvas = serverKvasBackend.GetUserClient(userId);
        var userEmailsSettings = await kvas.GetUserEmailsSettings(cancellationToken).ConfigureAwait(false);
        if (!userEmailsSettings.IsDigestEnabled)
            return JumpToEnd();

        var delay = await GetDelay(userId, cancellationToken).ConfigureAwait(false);
        return delay is null
            ? JumpToEnd()
            : Wait(nameof(OnTimer)).AddTimerEvent(delay.Value);
    }

    protected async Task<FlowTransition> OnTimer(CancellationToken cancellationToken)
    {
        var userId = UserId.Parse(Id.Id);

        var sendDigestCommand = new EmailsBackend_SendDigest(userId);
        var queues = Host.Services.Queues();
        await queues.Enqueue(sendDigestCommand, cancellationToken).ConfigureAwait(false);

        var delay = await GetDelay(userId, cancellationToken).ConfigureAwait(false);
        return delay is null
            ? JumpToEnd()
            : Wait(nameof(OnTimer)).AddTimerEvent(delay.Value);
    }

    private async Task<TimeSpan?> GetDelay(UserId userId, CancellationToken cancellationToken)
    {
        var accounts = Host.Services.GetRequiredService<IAccountsBackend>();
        var account = await accounts.Get(userId, cancellationToken).ConfigureAwait(false);
        account.Require(Account.MustExist);
        if (account.TimeZone.IsNullOrEmpty())
            return null;

        if (!TZConvert.TryGetTimeZoneInfo(account.TimeZone, out var timeZoneInfo)) {
            Host.Log.LogWarning("Unable to find time zone info. Time zone: '{TimeZone}'.", account.TimeZone);
            return null;
        }

        var serverKvasBackend = Host.Services.GetRequiredService<IServerKvasBackend>();
        var kvas = serverKvasBackend.GetUserClient(userId);
        var userEmailsSettings = await kvas.GetUserEmailsSettings(cancellationToken).ConfigureAwait(false);

        var timeSpan = Host.Clocks.SystemClock.UtcNow.DelayTo(userEmailsSettings.DigestTime, timeZoneInfo);
        return timeSpan;
    }
}
