using ActualChat.Module;
using ActualChat.Testing.Host;
using AspNet.Security.OAuth.Apple;

namespace ActualChat.Users.IntegrationTests;

[CollectionDefinition(nameof(UserCollection))]
public class UserCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("users", messageSink, TestAppHostOptions.WithDefaultChat with {
        ConfigureHost = (_, cfg) => {
            cfg.AddInMemory<CoreServerSettings>((x => x.AppleAppId, "com.test.app"));
        },
        ConfigureServices = (_, services) => {
            var handler = new AppleTokenEndpointHandlerMock();
            services.AddSingleton(handler);
            services.PostConfigure<AppleAuthenticationOptions>(
                AppleAuthenticationDefaults.AuthenticationScheme,
                opts => {
                    opts.ClaimsIssuer ??= AppleAuthenticationDefaults.AuthenticationScheme;
                    opts.Backchannel = new HttpClient(handler);
                    opts.ClientSecretGenerator = new AppleClientSecretGeneratorMock();
                });
        },
    });
