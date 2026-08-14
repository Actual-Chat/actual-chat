using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Bunit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

/// <summary>
/// Builds a <see cref="BunitContext"/> that can render components derived from
/// <see cref="ComponentBase{THub}"/> - i.e. anything reaching the localizer through its hub.
/// </summary>
internal static class TestBunitContext
{
    public static BunitContext New(Dictionary<string, string>? strings = null, Language? language = null)
    {
        // This is the smallest set of services UIHub's constructor reads eagerly.
        var hostInfo = new HostInfo {
            HostKind = HostKind.Server,
            AppKind = AppKind.Unknown,
            Environment = Environments.Development,
            BaseUrl = $"https://{Constants.Hosts.LocalVoxt}",
            IsTested = true,
        };
        var context = new BunitContext();
        context.Services
            .AddSingleton(_ => hostInfo)
            .AddSingleton(c => new Features(c))
            .AddSingleton(_ => new UrlMapper(hostInfo))
            .AddScoped<UIHub>()
            .AddFusion(fusion => fusion.AddBlazor());
        // ComputedStateComponent reaches its hub as (UIHub)CircuitHub - BlazorUICoreModule aliases
        // the two for the same reason. This has to come after AddBlazor, whose own CircuitHub
        // registration would otherwise win.
        context.Services.AddScoped<CircuitHub>(c => c.GetRequiredService<UIHub>());
        context.Services.AddSingleton<IStringLocalizer>(new TestStringLocalizer(strings ?? new(), language));
        return context;
    }
}
