using Stl.Redis;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection RegisterLocalIdGenerator<TDbContext, TEntity>(this IServiceCollection services)
    {
        services.AddSingleton(c => {
            var chatRedisDb = c.GetRequiredService<RedisDb<TDbContext>>();
            var name = nameof(TEntity);
            if (name.StartsWith("Db", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(2);
            return chatRedisDb.GetSequenceSet<TEntity>("seq." + name);
        });
        return services;
    }
}
