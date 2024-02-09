using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Search.IntegrationTests;

[CollectionDefinition(nameof(SearchCollection))]
public class SearchCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(messageSink)
{
    public override string DbInstanceName => "search";

    public override async Task InitializeAsync()
        => Host = await TestAppHostFactory.NewAppHost(MessageSink,
            DbInstanceName,
            TestAppHostOptions.WithDefaultChat with {
                AppConfigurationExtender = cfg => {
                    cfg.AddInMemory(
                        ("SearchSettings:IsSearchEnabled", "true"),
                        ("UsersSettings:NewAccountStatus", AccountStatus.Active.ToString()));
                },
            });
}
