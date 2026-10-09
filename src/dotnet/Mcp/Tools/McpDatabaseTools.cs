using System.ComponentModel;
using ActualChat.Mcp.Auth;
using ActualChat.Mcp.Models;
using ActualChat.Mui;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

// Admin-only: IMuiDb checks that the caller is an admin
[McpServerToolType]
public sealed class McpDatabaseTools(IServiceProvider services)
{
    private const int DefaultMaxRows = 200;

    private IMuiDb Db => field ??= services.GetRequiredService<IMuiDb>();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "get_database_schema", UseStructuredContent = true)]
    [Description("Admin only. Describes the databases the server can query with run_sql_query: for each one, "
        + "its tables and columns (\"name type\", \"?\" after the type when nullable, \"pk\" for key columns). "
        + "Names are PostgreSQL names in snake_case. Pass `database` to get one database only, and "
        + "`includeCreateScript` to also get its CREATE script (long).")]
    public async Task<McpDatabase[]> GetDatabaseSchema(
        [Description("Only this database, e.g. \"users\" or \"chat\".")] string? database = null,
        [Description("Include the CREATE script; it is long.")] bool includeCreateScript = false,
        CancellationToken cancellationToken = default)
    {
        var databases = await Db.GetDatabases(Session, cancellationToken).ConfigureAwait(false);
        return databases
            .Where(x => database.IsNullOrEmpty() || x.Name == database)
            .Select(x => new McpDatabase(
                x.Name,
                x.Kind,
                x.Tables.Select(t => new McpTable(t.Name, t.GetOrderedColumns().Select(FormatColumn).ToArray())).ToArray(),
                includeCreateScript ? x.CreateScript : null))
            .ToArray();
    }

    [McpServerTool(Name = "run_sql_query", UseStructuredContent = true)]
    [Description("Admin only. Runs one read-only SQL statement (SELECT, WITH ... SELECT, VALUES) on a database "
        + "listed by get_database_schema and returns the rows as text (NULL is null). Exactly one statement "
        + "is allowed; the query runs in a read-only transaction that is always rolled back, with a time limit, "
        + "and anything that would change data fails. Get the table and column names from "
        + "get_database_schema first. Keep results small: aggregate or LIMIT in SQL.")]
    public async Task<McpSqlResult> RunSqlQuery(
        [Description("Database name, e.g. \"users\" or \"chat\".")] string database,
        [Description("A single SQL statement.")] string sql,
        [Description("Max rows returned; capped at 500.")] int maxRows = DefaultMaxRows,
        CancellationToken cancellationToken = default)
    {
        var query = new MuiSqlQuery(database, sql, Math.Clamp(maxRows, 1, MuiSqlQuery.MaxRowsLimit));
        var result = await Db.Query(Session, query, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        return new McpSqlResult(
            result.Columns.Select(x => $"{x.Name} {x.Type}").ToArray(),
            result.Rows,
            result.IsTruncated,
            result.ElapsedMs);
    }

    // Private methods

    private static string FormatColumn(MuiColumnInfo column)
        => $"{column.Name} {column.Type}{(column.IsNullable ? "?" : "")}{(column.IsKey ? " pk" : "")}";
}
