using ActualChat.MLSearch.Module;
using ActualChat.Testing.Host;

namespace ActualChat.MLSearch.IntegrationTests;

[CollectionDefinition(nameof(MLSearchCollection))]
public class MLSearchCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : Testing.Host.AppHostFixture("ml_search", messageSink, TestAppHostOptions.Default with {
        ConfigureHost = (__, cfg) => {
            _ = cfg.AddInMemory<MLSearchSettings>((x => x.IsEnabled, "true"),
                (x => x.ChangedEntityIndexingDelay, "00:00:04"),
                (x => x.IndexingFlowResumeDelayQuanta, "00:00:01.5"));
        },
        ConfigureServices = (__, services) => {
            _ = services.AddSingleton<OpenSearchInit>()
                .AddAlias<IModuleInitializer, OpenSearchInit>()
                .AddSingleton<OpenSearchCleanup>();
        },
    })
{
    public override async Task<TestAppHost> NewAppHost(Func<TestAppHostOptions, TestAppHostOptions>? optionOverrider = null)
    {
        var appHost = await base.NewAppHost(optionOverrider);
        // Ensure cleanup service is instantiated
        if (!TestRunnerInfo.IsBuildAgent())
            _ = appHost.Services.GetRequiredService<OpenSearchCleanup>();
        return appHost;
    }
}
