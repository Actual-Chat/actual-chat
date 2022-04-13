using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Stl.Redis;

namespace ActualChat.Db;

public class DbLocalIdGeneratorFactory
{
    private readonly IServiceProvider _services;

    public DbLocalIdGeneratorFactory(IServiceProvider services)
        => _services = services;

    public DbLocalIdGeneratorFactory<TDbContext> For<TDbContext>()
        => new (_services);
}

public class DbLocalIdGeneratorFactory<TDbContext>
{
    private readonly IServiceProvider _services;

    public DbLocalIdGeneratorFactory(IServiceProvider services)
        => _services = services;

    public DbLocalIdGenerator<TDbContext, TEntity> New<TEntity>(
        Func<TDbContext,DbSet<TEntity>> dbSetExtractor,
        Expression<Func<TEntity,string>> shardKeySelector,
        Expression<Func<TEntity,long>> localIdSelector
    )
        where TEntity : class
    {
        var idSequences = _services.GetRequiredService<RedisSequenceSet<TEntity>>();
        return new DbLocalIdGenerator<TDbContext, TEntity>(
            idSequences,
            dbSetExtractor,
            shardKeySelector,
            localIdSelector);
    }
}
