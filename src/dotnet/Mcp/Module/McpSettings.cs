namespace ActualChat.Mcp.Module;

public sealed class McpSettings
{
    // Empty/null disables the MCP server entirely.
    public string Route { get; set; } = "/api/mcp";
}
