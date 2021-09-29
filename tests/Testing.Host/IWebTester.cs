using ActualChat.Host;
using Stl.Fusion.Authentication;

namespace ActualChat.Testing.Host
{
    public interface IWebTester : IDisposable
    {
        public AppHost AppHost { get; }
        public IServiceProvider AppServices { get; }
        public UriMapper UriMapper { get; }
        public IServerSideAuthService Auth { get; }
        public Session Session { get; }
    }
}
