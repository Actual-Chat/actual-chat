using System;
using ActualChat.Host;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Stl.Fusion;
using Stl.Fusion.Authentication;

namespace ActualChat.Testing
{
    public class BlazorTester : TestContext, IWebTester
    {
        private readonly IServiceScope _serviceScope;

        public AppHost AppHost { get; }
        public IServiceProvider AppServices => AppHost.Services;
        public IServiceProvider ScopedAppServices => _serviceScope!.ServiceProvider;
        public Session Session { get; }
        public UriMapper UriMapper => AppServices.UriMapper();
        public IServerSideAuthService Auth => AppServices.GetRequiredService<IServerSideAuthService>();

        public BlazorTester(AppHost appHost)
        {
            AppHost = appHost;
            _serviceScope = AppServices.CreateScope();
            Services.AddFallbackServiceProvider(ScopedAppServices);

            var sessionFactory = AppServices.GetRequiredService<ISessionFactory>();
            Session = sessionFactory.CreateSession();
            var sessionProvider = ScopedAppServices.GetRequiredService<ISessionProvider>();
            sessionProvider.Session = Session;

            Services.AddTransient(_ => ScopedAppServices.StateFactory());
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;
            _serviceScope.Dispose();
        }
    }
}
