using ActualChat.Testing.Host;
using ActualChat.Users.Module;
using AspNet.Security.OAuth.Apple;

namespace ActualChat.Users.IntegrationTests;

[CollectionDefinition(nameof(UserCollection))]
public class UserCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("users", messageSink, TestAppHostOptions.WithDefaultChat with {
        ConfigureHost = (_, cfg) => {
            cfg.AddInMemory<UsersSettings>(
                (x => x.AppleAppId, "com.test.app"),
                (x => x.PasskeyRpId, "localhost"),
                (x => x.PasskeyOrigins, "https://localhost"));
            // Every review-prompt threshold but the live-session one is lowered, so UsageTest can reach eligibility
            var reviewPrompt = $"{nameof(UsersSettings)}:{nameof(UsersSettings.ReviewPrompt)}";
            cfg.AddInMemoryCollection(
                ($"{reviewPrompt}:{nameof(ReviewPromptSettings.MinAccountAge)}", "00:00:00"),
                ($"{reviewPrompt}:{nameof(ReviewPromptSettings.MinActiveDays)}", "1"),
                ($"{reviewPrompt}:{nameof(ReviewPromptSettings.MinSpeechDuration)}", "00:00:00"));
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
