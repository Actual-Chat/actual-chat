using System.Security.Cryptography;
using ActualChat.Hashing;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Users.Phone;

/// <summary>
/// Keeps at most one pending code per (purpose, target): its hash, issue time and remaining
/// attempt count live in a single Redis record, so consuming an attempt and dropping the code once
/// they run out is one atomic operation. A new code overwrites the pending one - newest wins.
/// </summary>
public class TotpCodes(IServiceProvider services) : ITotpCodesBackend
{
    private const string KeyPrefix = ".TotpCode.";
    private const long Match = 1;
    private const long Exhausted = 3;
    private const string SetScript =
        """
        local key = KEYS[1]
        redis.call('DEL', key)
        redis.call('HSET', key, 'h', ARGV[1], 'n', ARGV[2], 't', ARGV[3])
        redis.call('PEXPIRE', key, ARGV[4])
        return 1
        """;
    private const string ValidateScript =
        """
        local key = KEYS[1]
        local candidate = ARGV[1]
        local hash = redis.call('HGET', key, 'h')
        if not hash then
            return { 0, 0 }
        end

        -- Full-length scan: the loop cost must not depend on where the first difference is
        local diff = 0
        if #hash ~= #candidate then
            diff = 1
        else
            for i = 1, #hash do
                if string.byte(hash, i) ~= string.byte(candidate, i) then
                    diff = diff + 1
                end
            end
        end
        if diff == 0 then
            redis.call('DEL', key)
            return { 1, 0 }
        end

        local remaining = redis.call('HINCRBY', key, 'n', -1)
        if remaining <= 0 then
            redis.call('DEL', key)
            return { 3, 0 }
        end
        return { 2, remaining }
        """;
    private static readonly int CodeCount = (int)Math.Pow(10, Constants.Auth.Phone.TotpLength);

    private RedisDb<UsersDbContext> RedisDb { get; } = services.GetRequiredService<RedisDb<UsersDbContext>>();
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log => field ??= services.LogFor(GetType());

    public virtual Task<int> Generate(
        string target,
        TotpPurpose purpose,
        CancellationToken cancellationToken = default)
        => Generate(target,
            purpose,
            Settings.TotpCodeLifetime,
            Settings.TotpMaxAttemptCount,
            cancellationToken);

    public virtual async Task<bool> Validate(
        string target,
        TotpPurpose purpose,
        int code,
        CancellationToken cancellationToken = default)
    {
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var result = await database
            .ScriptEvaluateAsync(
                ValidateScript,
                [ToKey(purpose, target)],
                [ToCodeHash(purpose, target, code)]
            ).ConfigureAwait(false);
        var values = (long[])result!;
        var status = values[0];
        if (status == Match)
            return true;

        if (status == Exhausted)
            Log.LogWarning("{Purpose}: no attempts left for target #{TargetHash}, pending code dropped",
                purpose, Hash(target));
        else
            Log.LogDebug("{Purpose}: check failed for target #{TargetHash}, status = {Status}, left = {Left}",
                purpose, Hash(target), status, values[1]);

        return false;
    }

    public virtual async Task<bool> IsEmailThrottled(
        string email, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var window = Settings.TotpUIThrottling;
        var emailKey = $".EmailTotpThrottle:email:{Hash(email)}";
        var isEmailAllowed = await db.StringSetAsync(emailKey, "1", window, When.NotExists).ConfigureAwait(false);
        return !isEmailAllowed;
    }

    public virtual async Task<bool> IsPhoneThrottled(
        ActualChat.Phone phone, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var window = Settings.TotpUIThrottling;
        var phoneKey = $".SmsTotpThrottle:phone:{Hash(phone.E164Value)}";
        var isPhoneAllowed = await db.StringSetAsync(phoneKey, "1", window, When.NotExists).ConfigureAwait(false);
        return !isPhoneAllowed;
    }

    public virtual async Task<TotpChannel?> GetLastChannel(
        ActualChat.Phone phone, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var value = await db.StringGetAsync(LastChannelKey(phone)).ConfigureAwait(false);
        return Enum.TryParse<TotpChannel>((string?)value, out var channel) ? channel : null;
    }

    public virtual async Task SetLastChannel(
        ActualChat.Phone phone, TotpChannel channel, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        await db.StringSetAsync(LastChannelKey(phone), channel.ToString(), Settings.TotpCodeLifetime)
            .ConfigureAwait(false);
    }

    // Protected/internal methods

    internal async Task<int> Generate(
        string target,
        TotpPurpose purpose,
        TimeSpan lifetime,
        int maxAttemptCount,
        CancellationToken cancellationToken)
    {
        var code = RandomNumberGenerator.GetInt32(0, CodeCount);
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        await database
            .ScriptEvaluateAsync(
                SetScript,
                [ToKey(purpose, target)],
                [
                    ToCodeHash(purpose, target, code),
                    maxAttemptCount,
                    (long)Clocks.SystemClock.Now.EpochOffset.TotalMilliseconds,
                    (long)lifetime.TotalMilliseconds,
                ]
            ).ConfigureAwait(false);
        return code;
    }

    // Private methods

    private static string LastChannelKey(ActualChat.Phone phone)
        => $".TotpLastChannel:{Hash(phone.E164Value)}";

    private static string ToKey(TotpPurpose purpose, string target)
        => $"{KeyPrefix}{purpose}.{Hash(target)}";

    private static string ToCodeHash(TotpPurpose purpose, string target, int code)
        => Hash($"{purpose}:{target}:{code.Format()}");

    private static string Hash(string value)
        => value.Hash().SHA256().ToBase64HashString(Hashing.HashAlgorithm.SHA256);
}
