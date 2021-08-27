using Microsoft.AspNetCore.Routing;

namespace ActualChat.Distribution.Module
{
    public interface IHubRegistrar
    {
        public void RegisterHubs(IEndpointRouteBuilder endpoints);
    }
}