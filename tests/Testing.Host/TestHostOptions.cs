using ActualChat.App.Server;
using ActualChat.App.Server.Initializers;
using ActualChat.Chat.Module;
using ActualChat.Testing.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Testing.Host;

public record TestAppHostOptions
{
    public static readonly TestAppHostOptions None = new();
    public static readonly TestAppHostOptions Default = new() {
        MustInitializeDb = true,
        MustStart = true,
    };
    public static readonly TestAppHostOptions WithDefaultChat = Default with {
        ChatDbInitializerOptions = new ChatDbInitializer.Options() {
            AddDefaultChat = true,
        },
    };
    public static readonly TestAppHostOptions WithAnnouncementsChat = Default with {
        ChatDbInitializerOptions = new ChatDbInitializer.Options() {
            AddAnnouncementsChat = true,
        },
    };
    public static readonly TestAppHostOptions WithAllChats = Default with {
        ChatDbInitializerOptions = new ChatDbInitializer.Options() {
            AddAnnouncementsChat = true,
            AddDefaultChat = true,
            AddNotesChat = true,
            AddFeedbackTemplateChat = true,
        },
    };

    public string InstanceName { get; init; } = "";
    public ITestOutputHelper Output { get; init; } = NullTestOutput.Instance;
    public string? ServerUrls { get; init; }
    public Action<AppHost.IConfigureHostContext, IConfigurationManager>? ConfigureHost { get; init; }
    public Action<AppHost.IConfigureModuleServicesContext, IServiceCollection>? ConfigureModuleServices { get; init; }
    public Action<AppHost.IConfigureServicesContext, IServiceCollection>? ConfigureServices { get; init; }
    public Action<AppHost.IConfigureAppContext, WebApplication>? ConfigureApp { get; set; }
    public DbInitializeOptions DbInitializeOptions { get; init; } = DbInitializeOptions.Default;
    public ChatDbInitializer.Options ChatDbInitializerOptions { get; init; } = ChatDbInitializer.Options.None;
    public bool? UseNatsQueues { get; init; }
    public bool MustCleanupRedis { get; init; }
    public bool MustInitializeDb { get; init; }
    public bool MustStart { get; init; }

    public TestAppHostOptions With(string instanceName, ITestOutputHelper output)
        => this with {
            InstanceName = instanceName,
            Output = output,
        };
    public TestAppHostOptions With(string instanceName, IMessageSink messageSink)
        => this with {
            InstanceName = instanceName,
            Output = new MessageSinkTestOutput(messageSink),
        };
}
