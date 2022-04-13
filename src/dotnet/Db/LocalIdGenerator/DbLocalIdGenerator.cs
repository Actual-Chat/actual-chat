using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Redis;

namespace ActualChat.Db;

public class DbLocalIdGenerator<TDbContext, TEntity> where TEntity : class
{
    private readonly ThreadSafeLruCache<Symbol, long> _maxLocalIdCache = new(16384);
    private readonly RedisSequenceSet<TEntity> _idSequences;
    private readonly Func<TDbContext, DbSet<TEntity>> _dbSetExtractor;
    private readonly Expression<Func<TEntity, string>> _shardKeySelector;
    private readonly Expression<Func<TEntity, long>> _localIdSelector;

    public DbLocalIdGenerator(RedisSequenceSet<TEntity> idSequences,
        Func<TDbContext,DbSet<TEntity>> dbSetExtractor,
        Expression<Func<TEntity,string>> shardKeySelector,
        Expression<Func<TEntity,long>> localIdSelector)
    {
        _idSequences = idSequences;
        _dbSetExtractor = dbSetExtractor;
        _shardKeySelector = shardKeySelector;
        _localIdSelector = localIdSelector;
    }

    private Expression<Func<TEntity, bool>> CreateShardFilterExpression(DbLocalIdQueryClosure closure)
    {
        var p = _shardKeySelector.Parameters[0];
        var field = Expression.Field(Expression.Constant(closure), DbLocalIdQueryClosure.ShardKeyFieldInfo);
        var body = Expression.Equal(_shardKeySelector.Body, field);
        var filterExpression = Expression.Lambda<Func<TEntity, bool>>(body, p);
        return filterExpression;
    }

    public async Task<long> Next(
        TDbContext dbContext,
        string shardKey,
        CancellationToken cancellationToken)
    {
        var idSequenceKey = new Symbol(shardKey);
        var maxLocalId = _maxLocalIdCache.GetValueOrDefault(idSequenceKey);
        if (maxLocalId == 0) {
            var closure = new DbLocalIdQueryClosure { ShardKey = shardKey };
            var filterExpression = CreateShardFilterExpression(closure);
            _maxLocalIdCache[idSequenceKey] = maxLocalId =
                await _dbSetExtractor(dbContext).ForUpdate() // To serialize inserts
                    .Where(filterExpression)
                    .OrderByDescending(_localIdSelector)
                    .Select(_localIdSelector)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
        }

        var localId = await _idSequences.Next(idSequenceKey, maxLocalId).ConfigureAwait(false);
        _maxLocalIdCache[idSequenceKey] = localId;
        return localId;
    }
}
