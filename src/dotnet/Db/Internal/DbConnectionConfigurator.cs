using System.Data.Common;
using ActualChat.Db.Module;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ActualChat.Db;

public class DbConnectionConfigurator(DbKind dbKind) : IDbConnectionInterceptor
{
    public void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (dbKind != DbKind.PostgreSql)
            return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SET enable_bitmapscan = OFF;
            SET enable_seqscan = OFF;
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (dbKind != DbKind.PostgreSql)
            return;

        var cmd = connection.CreateCommand();
        await using var _ = cmd.ConfigureAwait(false);
        cmd.CommandText =
            """
            SET enable_bitmapscan = OFF;
            SET enable_seqscan = OFF;
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
