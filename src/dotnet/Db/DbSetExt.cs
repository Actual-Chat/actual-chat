using Cysharp.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Db;

public static class DbSetExt
{
    public static DbContext GetDbContext<T>(this DbSet<T> dbSet)
        where T: class
        => dbSet.GetInfrastructure().GetRequiredService<ICurrentDbContext>().Context;

    public static string GetTableName<T>(this DbSet<T> dbSet)
        where T: class
    {
        var dbContext = dbSet.GetDbContext();
        var model = dbContext.Model;
        var entityTypes = model.GetEntityTypes();
        var entityType = entityTypes.Single(t => t.ClrType == typeof(T));
        var tableNameAnnotation = entityType.GetAnnotation("Relational:TableName");
        var tableName = tableNameAnnotation.Value!.ToString();
        return tableName!;
    }

    public static IQueryable<T> WithLockingClause<T>(this DbSet<T> dbSet, string hints)
        where T: class
    {
        var tableName = dbSet.GetTableName();
        var sql = $"{tableName}\" FOR {hints}";
        return dbSet.FromSqlRaw(sql);
    }

    public static IQueryable<T> WithLockingClause<T>(this DbSet<T> dbSet,
        DbLockingHint lockingHint,
        DbWaitHint waitHint,
        params string[] tableNames)
        where T: class
    {
        var sb = ZString.CreateStringBuilder();
        try {
            sb.Append("SELECT * FROM \"");
            sb.Append(dbSet.GetTableName());
            sb.Append('"');
            DbHintFormatter.AppendTo(ref sb, lockingHint, waitHint, tableNames);
            var sql = sb.ToString();
            return dbSet.FromSqlRaw(sql);
        }
        finally {
            sb.Dispose();
        }
    }

    public static IQueryable<T> ForShare<T>(this DbSet<T> dbSet, DbWaitHint waitHint = default)
        where T: class
        => dbSet.WithLockingClause(DbLockingHint.Share, waitHint);

    public static IQueryable<T> ForKeyShare<T>(this DbSet<T> dbSet, DbWaitHint waitHint = default)
        where T: class
        => dbSet.WithLockingClause(DbLockingHint.KeyShare, waitHint);

    public static IQueryable<T> ForUpdate<T>(this DbSet<T> dbSet, DbWaitHint waitHint = default)
        where T: class
        => dbSet.WithLockingClause(DbLockingHint.Update, waitHint);

    public static IQueryable<T> ForNoKeyUpdate<T>(this DbSet<T> dbSet, DbWaitHint waitHint = default)
        where T: class
        => dbSet.WithLockingClause(DbLockingHint.NoKeyUpdate, waitHint);
}
