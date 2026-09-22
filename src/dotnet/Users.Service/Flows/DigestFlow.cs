using ActualChat.Flows;
using ActualChat.Queues;
using TimeZoneConverter;

namespace ActualChat.Users.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

[Flow(DelayQuanta = 3600)] // 1 Hour
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class DigestFlow : PeriodicFlow
{
    private static readonly TimeSpan MinInactivity = TimeSpan.FromHours(24);

    protected override TimeSpan MaxResumeDelay => TimeSpan.FromDays(2);

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    private Account Account { get; set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    private TimeZoneInfo TimeZoneInfo { get; set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    private TimeSpan DigestTime { get; set; }

    protected override async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
        var userId = UserId.Parse(Id.Arguments);
        var accounts = Services.GetRequiredService<IAccountsBackend>();
        var account = await accounts.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account?.IsGuestOrNull() != false)
            return "No account";
        if (account.TimeZone.IsNullOrEmpty())
            return "Account has no time zone";
        if (!account.IsEmailVerified() && !await IsSystemOrBot(userId, cancellationToken).ConfigureAwait(false))
            return "Account has no verified email";
        if (!account.Email.EndsWith(Constants.Team.EmailSuffix, StringComparison.OrdinalIgnoreCase))
            return "Account is excluded";
        if (!TZConvert.TryGetTimeZoneInfo(account.TimeZone, out var timeZoneInfo))
            return $"Can't find TimeZoneInfo for time zone: {account.TimeZone}";

        var serverKvasBackend = Services.GetRequiredService<IServerKvasBackend>();
        var kvas = serverKvasBackend.ForUser(userId);
        var userEmailsSettings = await kvas.UserEmailsSettings().Get(cancellationToken).ConfigureAwait(false);
        if (!userEmailsSettings.IsDigestEnabled)
            return "Digest is disabled for this account";

        Account = account;
        TimeZoneInfo = timeZoneInfo;
        DigestTime = userEmailsSettings.DigestTime;
        return FlowReadiness.Ready;
    }

    protected override async ValueTask<Moment> Run(CancellationToken cancellationToken)
    {
        var nextRunAt = TimeZoneInfo.NextTimeOfDay(DigestTime, Hub.SystemNow);
        if (await IsSystemOrBot(Account.Id, cancellationToken).ConfigureAwait(false))
            return nextRunAt;
        if (await IsRecentlyActive(cancellationToken).ConfigureAwait(false)) {
            Console.Log("Skipped: the user was active recently");
            return nextRunAt;
        }

        var sendDigestCommand = new EmailsBackend_SendDigest(Account.Id);
        var queues = Services.Queues();
        await queues.Enqueue(sendDigestCommand, cancellationToken).ConfigureAwait(false);
        return nextRunAt;
    }

    private async Task<bool> IsRecentlyActive(CancellationToken cancellationToken)
    {
        // Someone who used the app since the last digest has already seen what it would summarize
        var userPresences = Services.GetRequiredService<IUserPresencesBackend>();
        var lastCheckIn = await userPresences.GetLastCheckIn(Account.Id, cancellationToken).ConfigureAwait(false);
        return lastCheckIn is { } at && Hub.SystemNow - at < MinInactivity;
    }

    private async Task<bool> IsSystemOrBot(UserId userId, CancellationToken cancellationToken)
    {
        if (Constants.User.SystemUserIds.Contains(userId))
            return true;

        var accounts = Services.GetRequiredService<IAccountsBackend>();
        var account = await accounts.Get(userId, cancellationToken).ConfigureAwait(false);
        return account is { IsBot: true };
    }
}
