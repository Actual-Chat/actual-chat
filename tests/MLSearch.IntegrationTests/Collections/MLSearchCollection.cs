using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Module;
using ActualChat.Testing.Host;

namespace ActualChat.MLSearch.IntegrationTests;

[CollectionDefinition(nameof(MLSearchCollection))]
public class MLSearchCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : Testing.Host.AppHostFixture("mlsearch", messageSink, TestAppHostOptions.Default with {
        ConfigureHost = (_, cfg) => {
            cfg.AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.IsEnabled)}", "true"));
            cfg.AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.IsInitialIndexingDisabled)}", "true"));
        },
        ConfigureServices = (_, cfg) => {
            cfg.AddSingleton(_ => new IndexNames {
                IndexPrefix = UniqueNames.Elastic("test"),
            });
        }
    });
