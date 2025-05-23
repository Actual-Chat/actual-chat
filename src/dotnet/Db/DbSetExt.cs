using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Db;

public static class DbSetExt
{
    public static Task<TEntity?> GetAsNoTracking<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        this DbSet<TEntity> set,
        string id,
        CancellationToken cancellationToken)
        where TEntity : class, IHasId<string>
    {
        id.RequireNonEmpty(nameof(id));
        return set.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public static ValueTask<TEntity?> Get<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        this DbSet<TEntity> set,
        string key,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        key.RequireNonEmpty(nameof(key));
        return set.FindAsync(DbKey.Compose(key), cancellationToken);
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

    // Lock

    public static Task Lock<TEntity>(
        this DbSet<TEntity> set,
        CancellationToken cancellationToken)
        where TEntity : class
        => set.GetDbContext().Lock(DbLockKey.New(typeof(TEntity)), cancellationToken);

    public static Task Lock<TEntity, TArg0>(
        this DbSet<TEntity> set,
        TArg0 arg0,
        CancellationToken cancellationToken)
        where TEntity : class
        => set.GetDbContext().Lock(DbLockKey.New(typeof(TEntity), arg0), cancellationToken);

    public static Task Lock<TEntity, TArg0, TArg1>(
        this DbSet<TEntity> set,
        TArg0 arg0,
        TArg1 arg1,
        CancellationToken cancellationToken)
        where TEntity : class
        => set.GetDbContext().Lock(DbLockKey.New(typeof(TEntity), arg0, arg1), cancellationToken);

    public static Task Lock<TEntity, TArg0, TArg1, TArg2>(
        this DbSet<TEntity> set,
        TArg0 arg0,
        TArg1 arg1,
        TArg2 arg2,
        CancellationToken cancellationToken)
        where TEntity : class
        => set.GetDbContext().Lock(DbLockKey.New(typeof(TEntity), arg0, arg1, arg2), cancellationToken);

    // LockShared

    public static Task LockShared<TEntity>(
        this DbSet<TEntity> set,
        CancellationToken cancellationToken)
        where TEntity : class
        => set.GetDbContext().LockShared(DbLockKey.New(typeof(TEntity)), cancellationToken);

    public static Task LockShared<TEntity, TArg0>(
        this DbSet<TEntity> set,
        TArg0 arg0,
        CancellationToken cancellationToken)
        where TEntity : class
        => set.GetDbContext().LockShared(DbLockKey.New(typeof(TEntity), arg0), cancellationToken);

    public static Task LockShared<TEntity, TArg0, TArg1>(
        this DbSet<TEntity> set,
        TArg0 arg0,
        TArg1 arg1,
        CancellationToken cancellationToken)
        where TEntity : class
        => set.GetDbContext().LockShared(DbLockKey.New(typeof(TEntity), arg0, arg1), cancellationToken);

    public static Task LockShared<TEntity, TArg0, TArg1, TArg2>(
        this DbSet<TEntity> set,
        TArg0 arg0,
        TArg1 arg1,
        TArg2 arg2,
        CancellationToken cancellationToken)
        where TEntity : class
        => set.GetDbContext().LockShared(DbLockKey.New(typeof(TEntity), arg0, arg1, arg2), cancellationToken);
}
