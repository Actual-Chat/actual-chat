using Microsoft.AspNetCore.Builder;

namespace ActualChat.Module;

/// <summary>
/// Module that configures web server endpoints and middleware.
/// </summary>
public interface IWebServerModule : IServerModule
{
    void ConfigureApp(WebApplication app);
}
