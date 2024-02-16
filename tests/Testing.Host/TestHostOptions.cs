using ActualChat.Chat.Module;
using ActualChat.Testing.Internal;
using Microsoft.AspNetCore.Hosting;
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
    public Action<IConfigurationBuilder>? HostConfigurationExtender { get; init; }
    public Action<IConfigurationBuilder>? AppConfigurationExtender { get; init; }
    public Action<WebHostBuilderContext, IServiceCollection>? AppServicesExtender { get; init; }
    public ChatDbInitializer.Options ChatDbInitializerOptions { get; init; } = ChatDbInitializer.Options.None;
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
