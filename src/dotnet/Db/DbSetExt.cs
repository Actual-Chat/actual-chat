using ActualChat.Db.Module;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

// ReSharper disable once CheckNamespace
namespace ActualChat;

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

    public static Task Lock<TEntity, TKey>(
        this DbSet<TEntity> set,
        TKey key,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode());
        return set.GetDbContext().ExecuteLock<TEntity>(lockKey, false, cancellationToken);
    }

    public static Task Lock<TEntity, TKey, TArg0>(
        this DbSet<TEntity> set,
        TKey key,
        TArg0 arg0,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode(), arg0);
        return set.GetDbContext().ExecuteLock<TEntity>(lockKey, false, cancellationToken);
    }

    public static Task Lock<TEntity, TKey, TArg0, TArg1>(
        this DbSet<TEntity> set,
        TKey key,
        TArg0 arg0,
        TArg1 arg1,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode(), arg0, arg1);
        return set.GetDbContext().ExecuteLock<TEntity>(lockKey, false, cancellationToken);
    }

    public static Task Lock<TEntity, TKey, TArg0, TArg1, TArg2>(
        this DbSet<TEntity> set,
        TKey key,
        TArg0 arg0,
        TArg1 arg1,
        TArg2 arg2,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode(), arg0, arg1, arg2);
        return set.GetDbContext().ExecuteLock<TEntity>(lockKey, false, cancellationToken);
    }

    public static Task SharedLock<TEntity, TKey>(
        this DbSet<TEntity> set,
        TKey key,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode());
        return set.GetDbContext().ExecuteLock<TEntity>(lockKey, true, cancellationToken);
    }

    public static Task SharedLock<TEntity, TKey, TArg0>(
        this DbSet<TEntity> set,
        TKey key,
        TArg0 arg0,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode(), arg0);
        return set.GetDbContext().ExecuteLock<TEntity>(lockKey, true, cancellationToken);
    }

    public static Task SharedLock<TEntity, TKey, TArg0, TArg1>(
        this DbSet<TEntity> set,
        TKey key,
        TArg0 arg0,
        TArg1 arg1,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode(), arg0, arg1);
        return set.GetDbContext().ExecuteLock<TEntity>(lockKey, true, cancellationToken);
    }

    public static Task SharedLock<TEntity, TKey, TArg0, TArg1, TArg2>(
        this DbSet<TEntity> set,
        TKey key,
        TArg0 arg0,
        TArg1 arg1,
        TArg2 arg2,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode(), arg0, arg1, arg2);
        return set.GetDbContext().ExecuteLock<TEntity>(lockKey, true, cancellationToken);
    }


    // Private methods

    private static async Task ExecuteLock<TEntity>(this DbContext context, long lockKey, bool isShared, CancellationToken cancellationToken)
        where TEntity : class
    {
        var timeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(DbSettings.LockTimeout);
        try {
            if (isShared)
                await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared({lockKey});", cancellationToken);
            else
                await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({lockKey});", cancellationToken);
        }
        catch (Exception ex) {
            StaticLog.For<DbSet<TEntity>>().LogError(ex, "Lock failed: {Type} {EntityType}({Key})", isShared ? "Shared" : "Exclusive", typeof(TEntity).Name, lockKey);
            throw;
        }
        finally {
            context.Database.SetCommandTimeout(timeout);
        }
    }
}
