using ActualChat.Hosting;
using Microsoft.AspNetCore.Builder;

namespace ActualChat.Module;

public interface IWebServerModule : IServerModule
{
    void ConfigureApp(IApplicationBuilder app);
}
