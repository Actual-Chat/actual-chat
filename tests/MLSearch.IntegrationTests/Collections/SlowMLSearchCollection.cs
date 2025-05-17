using ActualChat.Hosting;
using ActualChat.MLSearch.Module;
using ActualChat.Testing.Host;

namespace ActualChat.MLSearch.IntegrationTests;

[CollectionDefinition(nameof(SlowMLSearchCollection))]
public class SlowMLSearchCollection : ICollectionFixture<SlowAppHostFixture>;

public class SlowAppHostFixture(IMessageSink messageSink)
    : Testing.Host.AppHostFixture("slow_ml_search", messageSink, TestAppHostOptions.Default with {
        ConfigureHost = (__, cfg) => {
            _ = cfg.AddInMemory<MLSearchSettings>((x => x.IsEnabled, "true"),
                (x => x.IsInitialIndexingDisabled, "true"),
                (x => x.ChangedEntityIndexingDelay, "00:00:07"),
                (x => x.IndexingFlowResumeDelay, "00:00:02"));
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
