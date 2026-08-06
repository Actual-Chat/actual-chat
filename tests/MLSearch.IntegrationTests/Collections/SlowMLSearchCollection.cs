using ActualChat.MLSearch.Module;
using ActualChat.Testing.Host;

namespace ActualChat.MLSearch.IntegrationTests;

[CollectionDefinition(nameof(SlowMLSearchCollection))]
public class SlowMLSearchCollection : ICollectionFixture<SlowAppHostFixture>;

public class SlowAppHostFixture(IMessageSink messageSink)
    : Testing.Host.AppHostFixture("ml_search_slow", messageSink, TestAppHostOptions.Default with {
        ConfigureHost = (__, cfg) => {
            _ = cfg.AddInMemory<MLSearchSettings>((x => x.IsEnabled, "true"),
                (x => x.ChangedEntityIndexingDelay, TestRunnerInfo.IsBuildAgent() ? "00:00:10" : "00:00:03"),
                (x => x.IndexingFlowResumeDelayQuanta, TestRunnerInfo.IsBuildAgent() ? "00:00:03" : "00:00:01"));
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
