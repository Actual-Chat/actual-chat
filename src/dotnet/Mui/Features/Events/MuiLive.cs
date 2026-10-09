namespace ActualChat.Mui;

public sealed record MuiLiveRow(string Database, MuiSqlResult? Result, string? Error)
{
    public string?[]? Values => Result is { Rows: { Length: > 0 } rows } ? rows[0] : null;
}

// Shared by the real-time system pages (Events, Operations): one query per database, current state only
public static class MuiLive
{
    public const int RefreshSeconds = 5;

    public static string[] GetDatabaseNames(MuiDatabaseInfo[] databases, string tableName)
        => databases
            .Where(x => x.Tables.Any(t => t.Name == tableName))
            .Select(x => x.Name)
            .ToArray();

    public static Task<MuiLiveRow[]> Query(
        IMuiDb db, Session session, IEnumerable<string> databases, string sql, CancellationToken cancellationToken)
        => Task.WhenAll(databases.Select(x => QueryOne(db, session, x, sql, cancellationToken)));

    public static long ToLong(string? text)
        => long.TryParse(text, out var value) ? value : 0;

    public static double? ToSeconds(string? text)
        => double.TryParse(text, out var value) ? value : null;

    public static string FormatDuration(double? seconds)
    {
        if (seconds is not { } value)
            return "-";

        var total = (long)Math.Max(value, 0);
        if (total < 1)
            return "<1 s";
        if (total < 60)
            return $"{total} s";

        var minutes = total / 60;
        if (minutes < 60)
            return $"{minutes} min {total % 60} s";

        var hours = minutes / 60;
        if (hours < 24)
            return $"{hours} h {minutes % 60} min";

        return $"{hours / 24} d {hours % 24} h";
    }

    public static string FormatRate(long count, int windowSeconds)
        => count == 0 ? "0" : $"{count} ({(double)count / windowSeconds:0.00}/s)";

    private static async Task<MuiLiveRow> QueryOne(
        IMuiDb db, Session session, string database, string sql, CancellationToken cancellationToken)
    {
        try {
            var query = new MuiSqlQuery(database, sql, MuiSqlQuery.MaxRowsLimit, 10_000);
            var result = await db.Query(session, query, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
            return new MuiLiveRow(database, result, null);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            return new MuiLiveRow(database, null, e.Message);
        }
    }
}
