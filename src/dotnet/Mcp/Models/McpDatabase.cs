namespace ActualChat.Mcp.Models;

public sealed record McpDatabase(string Name, string Kind, McpTable[] Tables, string? CreateScript);

// Columns are written as "name type" with "?" after the type when nullable and "pk" for key columns
public sealed record McpTable(string Name, string[] Columns);

public sealed record McpSqlResult(
    string[] Columns,
    string?[][] Rows,
    bool IsTruncated,
    int ElapsedMs);
