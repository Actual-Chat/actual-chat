using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Redis;

namespace ActualChat.Db;

public static class DbContextBuilderExt
{
    public static DbContextBuilder<TDbContext> AddShardLocalIdGenerator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbContext,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>
    (
        this DbContextBuilder<TDbContext> dbContext,
        Func<TDbContext,DbSet<TEntity>> dbSetSelector,
        Expression<Func<TEntity, string, bool>> shardKeyFilter,
        Expression<Func<TEntity, long>> localIdSelector,
        Func<IThreadSafeLruCache<Symbol, long>>? maxLocalIdCacheFactory = null)
        where TDbContext : DbContext
        where TEntity : class
        => dbContext.AddShardLocalIdGenerator<TDbContext, TEntity, string>(
            dbSetSelector, shardKeyFilter, localIdSelector, maxLocalIdCacheFactory);

    public static DbContextBuilder<TDbContext> AddShardLocalIdGenerator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbContext,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TShardKey>
    (
        this DbContextBuilder<TDbContext> dbContext,
        Func<TDbContext,DbSet<TEntity>> dbSetSelector,
        Expression<Func<TEntity, TShardKey, bool>> shardKeyFilter,
        Expression<Func<TEntity, long>> localIdSelector,
        Func<IThreadSafeLruCache<Symbol, long>>? maxLocalIdCacheFactory = null)
        where TDbContext : DbContext
        where TEntity : class
        where TShardKey : class
    {
        var services = dbContext.Services;
        services.AddSingleton(c => {
            var redisDb = c.GetRequiredService<RedisDb<TDbContext>>();
            var sequenceSet = redisDb.GetSequenceSet<TEntity>($"seq.{typeof(TEntity)}");
            return sequenceSet;
        });
        services.AddSingleton(c => {
            var idSequences = c.GetRequiredService<RedisSequenceSet<TEntity>>();
            var generator = new DbShardLocalIdGenerator<TDbContext, TEntity, TShardKey>(
                idSequences, dbSetSelector, shardKeyFilter, localIdSelector, maxLocalIdCacheFactory);
            return (IDbShardLocalIdGenerator<TEntity, TShardKey>)generator;
        });
        return dbContext;
    }
}
