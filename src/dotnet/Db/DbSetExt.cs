using Microsoft.EntityFrameworkCore;

// ReSharper disable once CheckNamespace
namespace ActualChat;

public static class DbSetExt
{
    public static ValueTask<TEntity?> Get<TEntity>(
        this DbSet<TEntity> set,
        string key,
        CancellationToken cancellationToken) where TEntity : class
        => set.FindAsync(new object?[] { key }, cancellationToken);

    public static ValueTask<TEntity?> Get<TEntity>(
        this DbSet<TEntity> set,
        long key,
        CancellationToken cancellationToken) where TEntity : class
        => set.FindAsync(new object?[] { key }, cancellationToken);
}
