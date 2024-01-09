using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

// ReSharper disable once CheckNamespace
namespace ActualChat;

public static class DbSetExt
{
    public static ValueTask<TEntity?> Get<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        this DbSet<TEntity> set,
        Symbol key,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        key.RequireNonEmpty("key");
        return set.FindAsync(DbKey.Compose(key.Value), cancellationToken);
    }

    public static ValueTask<TEntity?> Get<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        this DbSet<TEntity> set,
        long key,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (key <= 0)
            throw new ArgumentOutOfRangeException(nameof(key), "Key must be greater than zero.");
        return set.FindAsync(DbKey.Compose(key), cancellationToken);
    }
}
