using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Redis;

namespace ActualChat.Users;

public static class LocalIdGeneratorFactory
{
    public static DbContextLocalIdGeneratorFactory<TDbContext> ForContext<TDbContext>(IServiceProvider svp)
        => new DbContextLocalIdGeneratorFactory<TDbContext>(svp);
}

public class DbContextLocalIdGeneratorFactory<TDbContext>
{
    private readonly IServiceProvider _svp;

    public LocalIdGenerator<TDbContext, TEntity> Create<TEntity>(
        Func<TDbContext,DbSet<TEntity>> dbSetExtractor,
        Expression<Func<TEntity,string>> shardKeySelector,
        Expression<Func<TEntity,long>> localIdSelector
    )
        where TEntity : class
    {
        var idSequences = _svp.GetRequiredService<RedisSequenceSet<TEntity>>();
        return new LocalIdGenerator<TDbContext, TEntity>(
            idSequences,
            dbSetExtractor,
            shardKeySelector,
            localIdSelector);
    }


    public DbContextLocalIdGeneratorFactory(IServiceProvider svp)
        => _svp = svp;
}

public class LocalIdGenerator<TDbContext, TEntity> where TEntity : class
{
    private readonly ThreadSafeLruCache<Symbol, long> _maxLocalIdCache = new(16384);
    private readonly RedisSequenceSet<TEntity> _idSequences;
    private readonly Func<TDbContext, DbSet<TEntity>> _dbSetExtractor;
    private readonly Expression<Func<TEntity, string>> _shardKeySelector;
    private readonly Expression<Func<TEntity, long>> _localIdSelector;

    public LocalIdGenerator(RedisSequenceSet<TEntity> idSequences,
        Func<TDbContext,DbSet<TEntity>> dbSetExtractor,
        Expression<Func<TEntity,string>> shardKeySelector,
        Expression<Func<TEntity,long>> localIdSelector)
    {
        _idSequences = idSequences;
        _dbSetExtractor = dbSetExtractor;
        _shardKeySelector = shardKeySelector;
        _localIdSelector = localIdSelector;
    }

    private Expression<Func<TEntity, bool>> CreateShardFilterExpression(string shardKey)
    {
        var p = _shardKeySelector.Parameters[0];
        var body = Expression.Equal(_shardKeySelector.Body, Expression.Constant(shardKey));
        var filterExpression = Expression.Lambda<Func<TEntity, bool>>(body, p);
        return filterExpression;
    }

    public async Task<long> DbNextLocalId(
        TDbContext dbContext,
        string shardKey,
        CancellationToken cancellationToken)
    {
        var idSequenceKey = new Symbol(shardKey);
        var maxLocalId = _maxLocalIdCache.GetValueOrDefault(idSequenceKey);
        if (maxLocalId == 0) {
            var filterExpression = CreateShardFilterExpression(shardKey);
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
