using ActualChat.Host;
using ActualChat.Testing.Collections;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Testing.Host;

[Collection(nameof(AppHostTests)), Trait("Category", nameof(AppHostTests))]
public class AppHostTestBase : TestBase
{
    public AppHostTestBase(ITestOutputHelper @out) : base(@out) { }

    protected Task<AppHost> NewAppHost(
        Action<IConfigurationBuilder>? configureAppSettings = null,
        Action<IServiceCollection>? configureServices = null,
        string? serverUrls = null)
        => TestHostFactory.NewAppHost(Out, configureAppSettings, configureServices, serverUrls);
}
