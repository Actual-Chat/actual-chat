using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Stl.Redis;

namespace ActualChat.Db;

public class LocalIdGeneratorFactory
{
    private readonly IServiceProvider _svp;

    public LocalIdGeneratorFactory(IServiceProvider svp)
        => _svp = svp;

    public LocalIdGeneratorFactory<TDbContext> ForContext<TDbContext>()
        => new LocalIdGeneratorFactory<TDbContext>(_svp);
}

public class LocalIdGeneratorFactory<TDbContext>
{
    private readonly IServiceProvider _svp;

    public LocalIdGeneratorFactory(IServiceProvider svp)
        => _svp = svp;

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
}
