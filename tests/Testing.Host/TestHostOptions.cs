using ActualChat.Chat.Module;
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
        },
    };

    public string? ServerUrls { get; init; } = null;
    public Action<IConfigurationBuilder>? HostConfigurationExtender { get; init; } = null;
    public Action<IConfigurationBuilder>? AppConfigurationExtender { get; init; } = null;
    public Action<WebHostBuilderContext, IServiceCollection>? AppServicesExtender { get; init; } = null;
    public ChatDbInitializer.Options ChatDbInitializerOptions { get; init; } = ChatDbInitializer.Options.None;
    public bool MustInitializeDb { get; init; }
    public bool MustStart { get; init; }
}
