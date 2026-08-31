using ActualChat.Live;
using ActualChat.Redis;
using ActualLab.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

public partial class TypingIndicatorsBackend : ShardComputeService, ITypingIndicatorsBackend
{
    // Typing is high-churn and short-lived: a client re-emits while typing, and a streak lapses
    // on its own shortly after the last keystroke - no explicit stop needed.
    private static readonly TimeSpan HashTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TypingStaleness = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan SelfHealDelay = TimeSpan.FromSeconds(6);

    private readonly RedisMultiHashMap<TypingInfo> _typists;

    public TypingIndicatorsBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        _typists = new RedisMultiHashMap<TypingInfo>(redisDb, "typing:indicators", Log) {
            HashTtl = HashTtl,
        };
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> ListTypingAuthorIds(ChatId chatId, CancellationToken cancellationToken)
    {
        // Captured before the awaits below — see LiveSessionsBackend.GetState.
        var computed = Computed.GetCurrent();
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var cutoff = Clocks.SystemClock.Now - TypingStaleness;
        var typists = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var authorIds = typists
            .Where(kv => IsFresh(kv.Value, cutoff))
            .Select(kv => (Ok: AuthorId.TryParse(kv.Key, out var id), Id: id, kv.Value!.StartedAt))
            .Where(x => x.Ok)
            .OrderBy(x => x.StartedAt)
            .Select(x => x.Id)
            .ToApiArray();
        if (authorIds.Count > 0)
            // Re-check so a typist who stopped without an explicit off signal drops on its own.
            computed.Invalidate(SelfHealDelay);
        return authorIds;
    }

    public virtual async Task SetTyping(
        ChatId chatId,
        AuthorId authorId,
        TypingKind kind,
        bool isActive,
        CancellationToken cancellationToken)
    {
        if (isActive) {
            var now = Clocks.SystemClock.Now;
            var existing = await SafeGet(chatId, authorId).ConfigureAwait(false);
            // Preserve the streak's start across re-emits so ordering by "who started first" is stable;
            // a kind change (Typing -> SendingFiles) stays the same streak.
            var startedAt = existing is { StartedAt: var s } && s != default ? s : now;
            await SafeSet(chatId, authorId, new TypingInfo(kind, startedAt, now)).ConfigureAwait(false);
        }
        else
            await SafeRemove(chatId, authorId).ConfigureAwait(false);
        InvalidateListTypingAuthorIds(chatId);
    }

    // Private methods

    private async Task<TypingInfo?> SafeGet(ChatId chatId, AuthorId authorId)
    {
        try {
            return await _typists.Get(chatId.Value, authorId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read typists from Redis for chat #{ChatId}", chatId);
            return null;
        }
    }

    private async Task<Dictionary<string, TypingInfo?>> SafeGetHashMap(ChatId chatId)
    {
        try {
            return await _typists.GetHashMap(chatId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read typists from Redis for chat #{ChatId}", chatId);
            return [];
        }
    }

    private async Task SafeSet(ChatId chatId, AuthorId authorId, TypingInfo info)
    {
        try {
            await _typists.Set(chatId.Value, authorId.Value, info).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to write typist to Redis for chat #{ChatId}", chatId);
        }
    }

    private async Task SafeRemove(ChatId chatId, AuthorId authorId)
    {
        try {
            await _typists.Remove(chatId.Value, authorId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to remove typist from Redis for chat #{ChatId}", chatId);
        }
    }

    private static bool IsFresh(TypingInfo? info, Moment cutoff)
        => info is not null && info.RegisteredAt >= cutoff;

    private void InvalidateListTypingAuthorIds(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = ListTypingAuthorIds(chatId, default);
    }

    // Nested types

    [DataContract, MessagePackObject]
    public sealed partial record TypingInfo(
        [property: DataMember(Order = 0), Key(0)] TypingKind Kind,
        [property: DataMember(Order = 1), Key(1)] Moment StartedAt,
        [property: DataMember(Order = 2), Key(2)] Moment RegisteredAt);
}
