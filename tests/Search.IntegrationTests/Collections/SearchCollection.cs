using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Search.IntegrationTests;

[CollectionDefinition(nameof(SearchCollection))]
public class SearchCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture("search", messageSink)
{
    protected override TestAppHostOptions CreateHostOptions()
        => base.CreateHostOptions() with {
            AppConfigurationExtender = cfg => {
                cfg.AddInMemory(
                    ("SearchSettings:IsSearchEnabled", "true"),
                    ("UsersSettings:NewAccountStatus", AccountStatus.Active.ToString()));
            },
        };
}
