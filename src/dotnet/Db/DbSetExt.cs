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

    public static async Task Lock<TEntity, TKey>(
        this DbSet<TEntity> set,
        TKey key,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var context = set.GetDbContext();
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode());
        var timeout = context.Database.GetCommandTimeout();
        // TODO(AK): find a way to resolve DBSettings instance
        context.Database.SetCommandTimeout(30);
        try {
            await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({lockKey});", cancellationToken);
        }
        catch (Exception ex) {
            // TODO(AK): find a way to resolve a logger
            await Console.Error.WriteLineAsync("Lock failed: " + ex.Message);
            throw;
        }
        finally {
            context.Database.SetCommandTimeout(timeout);
        }
    }

    public static async Task Lock<TEntity, TKey, TArg0>(
        this DbSet<TEntity> set,
        TKey key,
        TArg0 arg0,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var context = set.GetDbContext();
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode(), arg0);
        var timeout = context.Database.GetCommandTimeout();
        try {
            await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({lockKey});", cancellationToken);
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync("Lock failed: " + ex.Message);
            throw;
        }
        finally {
            context.Database.SetCommandTimeout(timeout);
        }
    }

    public static async Task Lock<TEntity, TKey, TArg0, TArg1>(
        this DbSet<TEntity> set,
        TKey key,
        TArg0 arg0,
        TArg1 arg1,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var context = set.GetDbContext();
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode(), arg0, arg1);
        var timeout = context.Database.GetCommandTimeout();
        try {
            await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({lockKey});", cancellationToken);
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync("Lock failed: " + ex.Message);
            throw;
        }
        finally {
            context.Database.SetCommandTimeout(timeout);
        }
    }

    public static async Task Lock<TEntity, TKey, TArg0, TArg1, TArg2>(
        this DbSet<TEntity> set,
        TKey key,
        TArg0 arg0,
        TArg1 arg1,
        TArg2 arg2,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var context = set.GetDbContext();
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode(), arg0, arg1, arg2);
        var timeout = context.Database.GetCommandTimeout();
        try {
            await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({lockKey});", cancellationToken);
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync("Lock failed: " + ex.Message);
            throw;
        }
        finally {
            context.Database.SetCommandTimeout(timeout);
        }
    }

    public static async Task SharedLock<TEntity, TKey>(
        this DbSet<TEntity> set,
        TKey key,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var context = set.GetDbContext();
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode());
        var timeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(30);
        try {
            await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared({lockKey});", cancellationToken);
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync("Lock failed: " + ex.Message);
            throw;
        }
        finally {
            context.Database.SetCommandTimeout(timeout);
        }
    }

    public static async Task SharedLock<TEntity, TKey, TArg0>(
        this DbSet<TEntity> set,
        TKey key,
        TArg0 arg0,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var context = set.GetDbContext();
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode(), arg0);
        var timeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(30);
        try {
            await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared({lockKey});", cancellationToken);
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync("Lock failed: " + ex.Message);
            throw;
        }
        finally {
            context.Database.SetCommandTimeout(timeout);
        }
    }

    public static async Task SharedLock<TEntity, TKey, TArg0, TArg1>(
        this DbSet<TEntity> set,
        TKey key,
        TArg0 arg0,
        TArg1 arg1,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var context = set.GetDbContext();
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode(), arg0, arg1);
        var timeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(30);
        try {
            await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared({lockKey});", cancellationToken);
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync("Lock failed: " + ex.Message);
            throw;
        }
        finally {
            context.Database.SetCommandTimeout(timeout);
        }
    }

    public static async Task SharedLock<TEntity, TKey, TArg0, TArg1, TArg2>(
        this DbSet<TEntity> set,
        TKey key,
        TArg0 arg0,
        TArg1 arg1,
        TArg2 arg2,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var context = set.GetDbContext();
        var keyHash = (long)(key?.GetHashCode() ?? 239);
        var lockKey = (keyHash << 32) ^ HashCode.Combine(typeof(TEntity).GetHashCode(), arg0, arg1, arg2);
        var timeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(30);
        try {
            await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared({lockKey});", cancellationToken);
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync("Lock failed: " + ex.Message);
            throw;
        }
        finally {
            context.Database.SetCommandTimeout(timeout);
        }
    }
}
