using ActualChat.OAuth.Module;
using ActualChat.Testing.Host;

namespace ActualChat.OAuth.IntegrationTests;

[CollectionDefinition(nameof(OAuthCollection))]
public class OAuthCollection : ICollectionFixture<OAuthCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("oauth", messageSink, TestAppHostOptions.Default with {
            ConfigureHost = (_, cfg) => cfg.AddInMemory<OAuthSettings>(
                (x => x.AccessTokenLifetime, "00:00:03"),
                (x => x.RefreshTokenReuseLeeway, "00:00:00"),
                (x => x.AllowInsecureClientMetadata, "true")),
        });
}
