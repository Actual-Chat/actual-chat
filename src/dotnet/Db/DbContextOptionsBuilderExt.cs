using ActualChat.Db.Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ActualChat.Db;

public static class DbContextOptionsBuilderExt
{
    public static DbContextOptionsBuilder UseNpgsqlConflictStrategies(this DbContextOptionsBuilder builder)
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));

        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new UpdateSqlGeneratorExtensions<NpgsqlUpdateSqlGenerator>());

        return builder;
    }
}
