using ActualChat.Flows;
using ActualChat.Queues;
using TimeZoneConverter;

namespace ActualChat.Users.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

[Flow(DelayQuanta = 3600)] // 1 Hour
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class DigestFlow : PeriodicFlow
{
    protected override TimeSpan MaxResumeDelay => TimeSpan.FromDays(2);

    [IgnoreDataMember, MemoryPackIgnore]
    private Account Account { get; set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore]
    private TimeZoneInfo TimeZoneInfo { get; set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore]
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
        if (!account.IsEmailVerified() && !IsSystemUser(userId))
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
        if (!IsSystemUser(Account.Id)) {
            var sendDigestCommand = new EmailsBackend_SendDigest(Account.Id);
            var queues = Services.Queues();
            await queues.Enqueue(sendDigestCommand, cancellationToken).ConfigureAwait(false);
        }
        return TimeZoneInfo.NextTimeOfDay(DigestTime, Hub.SystemNow);
    }

    private static bool IsSystemUser(UserId userId)
        => Constants.User.SystemUserIds.Contains(userId);
}
