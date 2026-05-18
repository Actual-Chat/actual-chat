namespace ActualChat.Mcp.Module;

public sealed class McpSettings
{
    public bool IsEnabled { get; set; } = true;
    public string Route { get; set; } = "/api/mcp";
    public string ServerName { get; set; } = "ActualChat";
    public string ServerVersion { get; set; } = "1.0.0";
}
