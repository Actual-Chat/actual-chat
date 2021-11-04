using ActualChat.Host;

namespace ActualChat.Testing.Host
{
    public interface IWebTester : IDisposable
    {
        public AppHost AppHost { get; }
        public IServiceProvider AppServices { get; }
        public UriMapper UriMapper { get; }
        public IAuth Auth { get; }
        public IAuthBackend AuthBackend { get; }
        public Session Session { get; }
    }
}
