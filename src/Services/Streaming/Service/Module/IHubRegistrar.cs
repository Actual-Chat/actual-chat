using Microsoft.AspNetCore.Routing;

namespace ActualChat.Streaming.Module
{
    public interface IHubRegistrar
    {
        public void RegisterHubs(IEndpointRouteBuilder endpoints);
    }
}