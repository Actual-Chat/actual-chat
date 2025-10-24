using ActualChat.Flows;
using ActualChat.Queues;
using MemoryPack;
using TimeZoneConverter;

namespace ActualChat.Users.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class DigestFlow : PeriodicFlow
{
    [IgnoreDataMember, MemoryPackIgnore]
    private UserId UserId { get; set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore]
    private TimeZoneInfo TimeZoneInfo { get; set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore]
    private TimeSpan DigestTime { get; set; }

    protected override Task<LegacyFlowTransition> OnReset(CancellationToken cancellationToken)
    {
        MaxDelay = TimeSpan.FromDays(2);
        return base.OnReset(cancellationToken);
    }

    protected override async Task<string?> Update(CancellationToken cancellationToken)
    {
        var userId = UserId.Parse(Id.Arguments);
        var accounts = Host.Services.GetRequiredService<IAccountsBackend>();
        var account = await accounts.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account?.IsGuestOrNull() != false)
            return "No account";
        if (account.TimeZone.IsNullOrEmpty())
            return "Account has no time zone";
        if (!account.HasVerifiedEmail())
            return "Account has no verified email";
        if (!account.Email.EndsWith(Constants.Team.EmailSuffix, StringComparison.OrdinalIgnoreCase))
            return "Account is excluded";
        if (!TZConvert.TryGetTimeZoneInfo(account.TimeZone, out var timeZoneInfo))
            return $"Can't find TimeZoneInfo for time zone: {account.TimeZone}";

        var serverKvasBackend = Host.Services.GetRequiredService<IServerKvasBackend>();
        var kvas = serverKvasBackend.GetUserClient(userId);
        var userEmailsSettings = await kvas.GetUserEmailsSettings(cancellationToken).ConfigureAwait(false);
        if (!userEmailsSettings.IsDigestEnabled)
            return "Digest is disabled for this account";

        UserId = userId;
        TimeZoneInfo = timeZoneInfo;
        DigestTime = userEmailsSettings.DigestTime;
        return null;
    }

    protected override Task Run(CancellationToken cancellationToken)
    {
        var sendDigestCommand = new EmailsBackend_SendDigest(UserId);
        var queues = Host.Services.Queues();
        return queues.Enqueue(sendDigestCommand, cancellationToken);
    }

    protected override Moment ComputeNextRunAt(Moment now, CancellationToken cancellationToken)
        => TimeZoneInfo.NextTimeOfDay(DigestTime, LastRunAt);
}
