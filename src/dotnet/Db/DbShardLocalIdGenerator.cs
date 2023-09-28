using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using ActualChat.Expressions;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Redis;

namespace ActualChat.Db;

public interface IDbShardLocalIdGenerator<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] in TShardKey>
    where TShardKey : class
{
    Task<long> Next(DbContext dbContext, TShardKey shardKey, CancellationToken cancellationToken);
}

public class DbShardLocalIdGenerator<
    TDbContext,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TShardKey>
    : IDbShardLocalIdGenerator<TEntity, TShardKey>
    where TDbContext : DbContext
    where TEntity : class
    where TShardKey : class
{
    private readonly IThreadSafeLruCache<Symbol, long> _maxLocalIdCache;
    private readonly RedisSequenceSet<TEntity> _idSequences;
    private readonly Func<TDbContext, DbSet<TEntity>> _dbSetExtractor;
    private readonly Expression<Func<TEntity, TShardKey, bool>> _shardKeyFilter;
    private readonly Expression<Func<TEntity, long>> _localIdSelector;

    public DbShardLocalIdGenerator(RedisSequenceSet<TEntity> idSequences,
        Func<TDbContext,DbSet<TEntity>> dbSetExtractor,
        Expression<Func<TEntity, TShardKey, bool>> shardKeyFilter,
        Expression<Func<TEntity, long>> localIdSelector,
        Func<IThreadSafeLruCache<Symbol, long>>? maxLocalIdCacheFactory = null)
    {
        _idSequences = idSequences;
        _dbSetExtractor = dbSetExtractor;
        _shardKeyFilter = shardKeyFilter;
        _localIdSelector = localIdSelector;
        _maxLocalIdCache = maxLocalIdCacheFactory?.Invoke() ?? new ConcurrentLruCache<Symbol, long>(16384);
    }

    public async Task<long> Next(DbContext dbContext, TShardKey shardKey, CancellationToken cancellationToken)
    {
        // This method must be thread-safe!

        var idSequenceKey = new Symbol(shardKey.ToString() ?? "");
        var maxLocalId = _maxLocalIdCache.GetValueOrDefault(idSequenceKey);
        if (maxLocalId == 0) {
            var shardFilterExpr = CreateShardFilterExpression(shardKey);
            _maxLocalIdCache[idSequenceKey] = maxLocalId =
                await _dbSetExtractor((TDbContext)dbContext)
                    .Where(shardFilterExpr)
                    .OrderByDescending(_localIdSelector)
                    .Select(_localIdSelector)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
        }

        var localId = await _idSequences.Next(idSequenceKey, maxLocalId).ConfigureAwait(false);
        _maxLocalIdCache[idSequenceKey] = localId;
        return localId;
    }

    private Expression<Func<TEntity, bool>> CreateShardFilterExpression(TShardKey shardKey)
    {
        var pShardKey = _shardKeyFilter.Parameters[1];
        var eShardKey = Expression.Constant(shardKey);
        return _shardKeyFilter.InlineParameter<Func<TEntity, TShardKey, bool>, Func<TEntity, bool>>(pShardKey, eShardKey);
    }
}
