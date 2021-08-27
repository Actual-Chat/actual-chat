using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace ActualChat.Distribution.Module
{
    public sealed class HubRegistrar : IHubRegistrar
    {
        public void RegisterHubs(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapHub<StreamingServiceHub>("/api/stream");
        }
    }
}