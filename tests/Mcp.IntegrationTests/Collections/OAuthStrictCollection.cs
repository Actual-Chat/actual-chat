using ActualChat.OAuth.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

// Same host as OAuthCollection minus AllowInsecureClientMetadata, so the CIMD host checks are live
[CollectionDefinition(nameof(OAuthStrictCollection))]
public class OAuthStrictCollection : ICollectionFixture<OAuthStrictCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("oauth-strict", messageSink, TestAppHostOptions.Default with {
            ConfigureHost = (_, cfg) => cfg.AddInMemory<OAuthSettings>(
                (x => x.AccessTokenLifetime, "00:00:03"),
                (x => x.RefreshTokenReuseLeeway, "00:00:00")),
        });
}
