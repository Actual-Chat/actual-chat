using ActualChat.App.Server;
using ActualChat.Testing.Collections;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Testing.Host;

[Collection(nameof(AppHostTests)), Trait("Category", nameof(AppHostTests))]
public class AppHostTestBase(ITestOutputHelper @out) : TestBase(@out)
{
    protected Task<AppHost> NewAppHost(TestAppHostConfiguration configurator) => TestHostFactory.NewAppHost(Out, configurator.ConfigureAppSettings, configurator.ConfigureServices, configurator.ServerUrls);

    protected Task<AppHost> NewAppHost(
        Action<IConfigurationBuilder>? configureAppSettings = null,
        Action<IServiceCollection>? configureServices = null,
        string? serverUrls = null)
        => TestHostFactory.NewAppHost(Out, configureAppSettings, configureServices, serverUrls);
}
