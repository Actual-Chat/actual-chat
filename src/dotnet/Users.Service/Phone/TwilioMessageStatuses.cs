using ActualChat.Diagnostics;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Redis;

namespace ActualChat.Users.Phone;

public class TwilioMessageStatuses(IServiceProvider services)
{
    public const string Route = "api/webhooks/twilio/message-status";
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);
    private const string UpdateScript =
        """
        local key = KEYS[1]
        if redis.call('EXISTS', key) == 0 then return 0 end
        if redis.call('HGET', key, 'account') ~= ARGV[1] then return 0 end
        local sid = redis.call('HGET', key, 'sid')
        if sid and sid ~= '' and sid ~= ARGV[2] then return 0 end
        redis.call('HSET', key, 'sid', ARGV[2])
        local rank = tonumber(redis.call('HGET', key, 'rank') or '0')
        local nextRank = tonumber(ARGV[4])
        if rank >= 5 or nextRank <= rank then return 1 end
        redis.call('HSET', key, 'status', ARGV[3], 'rank', ARGV[4], 'error', ARGV[5])
        return 2
        """;

    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private RedisDb<UsersDbContext> RedisDb => field ??= services.GetRequiredService<RedisDb<UsersDbContext>>();
    private ILogger Log { get; } = services.LogFor<TwilioMessageStatuses>();

    public Uri GetCallbackUri(string attemptId)
    {
        if (!Uri.TryCreate(Settings.TwilioStatusCallbackUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.Query.IsNullOrEmpty() || !uri.Fragment.IsNullOrEmpty()
            || !uri.UserInfo.IsNullOrEmpty() || uri.AbsolutePath != $"/{Route}"
            || Settings.TwilioAuthToken.IsNullOrEmpty())
            throw StandardError.Constraint("Twilio status callback configuration is invalid.");

        return new Uri($"{Settings.TwilioStatusCallbackUrl}?attemptId={Uri.EscapeDataString(attemptId)}");
    }

    public virtual async Task Begin(string attemptId, CancellationToken cancellationToken = default)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var result = await db.ScriptEvaluateAsync(
            """
            if redis.call('EXISTS', KEYS[1]) == 1 then return 0 end
            redis.call('HSET', KEYS[1], 'account', ARGV[1], 'status', 'pending', 'rank', '0', 'sid', '')
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
            return 1
            """,
            [ToKey(attemptId)], [Settings.TwilioAccountSid, (long)Lifetime.TotalMilliseconds]).ConfigureAwait(false);
        if ((long)result != 1)
            throw StandardError.Constraint("Twilio verification attempt already exists.");
    }

    public virtual async Task<bool> Update(
        string attemptId,
        string accountSid,
        string messageSid,
        string status,
        string? errorCode,
        CancellationToken cancellationToken = default)
    {
        var rank = GetStatusRank(status);
        if (rank == 0 || accountSid != Settings.TwilioAccountSid || messageSid.IsNullOrEmpty())
            return false;

        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var result = await db.ScriptEvaluateAsync(UpdateScript, [ToKey(attemptId)],
            [accountSid, messageSid, status, rank, errorCode ?? ""]).ConfigureAwait(false);
        var isMatched = (long)result > 0;
        if ((long)result == 2)
            AppMeters.VerificationCodeDeliveryStatus.Add(1,
                new KeyValuePair<string, object?>("provider", "twilio"),
                new KeyValuePair<string, object?>("channel", "Sms"),
                new KeyValuePair<string, object?>("status", status));
        Log.LogInformation("Twilio status {Status} for message {MessageSid}, attempt {AttemptId}, matched {IsMatched}",
            status, messageSid, attemptId, isMatched);

        return isMatched;
    }

    public virtual async Task<string?> GetStatus(string attemptId, CancellationToken cancellationToken = default)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var status = await db.HashGetAsync(ToKey(attemptId), "status").ConfigureAwait(false);

        return status.IsNull ? null : status.ToString();
    }

    public static int GetStatusRank(string status)
        => status switch {
            "accepted" => 1,
            "queued" => 2,
            "sending" => 3,
            "sent" => 4,
            "delivered" or "undelivered" or "failed" or "canceled" => 5,
            _ => 0,
        };

    private static string ToKey(string attemptId) => $".TwilioMessageStatus:{attemptId}";
}
