namespace ActualChat.Mcp.Module;

public sealed class McpSettings
{
    // Empty/null disables the MCP server entirely.
    public string Route { get; set; } = "/mcp";
    // Where the endpoint lived before /mcp; kept so already configured clients keep working.
    public string LegacyRoute { get; set; } = "/api/mcp";

    public IEnumerable<string> Routes {
        get {
            if (Route.IsNullOrEmpty())
                yield break;

            yield return Route;
            if (!LegacyRoute.IsNullOrEmpty())
                yield return LegacyRoute;
        }
    }
}
