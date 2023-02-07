using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.WebM;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App; // Keep it: it lets <Project Sdk="Microsoft.NET.Sdk.Razor"> compile
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration; // Keep it: it lets <Project Sdk="Microsoft.NET.Sdk.Razor"> compile
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Interception.Interceptors;

namespace ActualChat.App.Wasm;

public static class Program
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WasmApp))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TypeViewInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TypeView))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TypeView<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TypeView<,>))]
    public static async Task Main(string[] args)
    {
        var trace = TraceSession.Default = TraceSession.IsTracingEnabled
            ? TraceSession.New("main").ConfigureOutput(m => Console.Out.WriteLine(m)).Start()
            : TraceSession.Null;
        trace.Track("Wasm.Program.Main");
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        var baseUrl = builder.HostEnvironment.BaseAddress;
        builder.Services.TryAddSingleton<ITraceSession>(trace);
        var step = trace.TrackStep("ConfigureServices");
        await ConfigureServices(builder.Services, builder.Configuration, baseUrl).ConfigureAwait(false);
        step.Complete();

        step = trace.TrackStep("Building wasm host");
        var host = builder.Build();
        step.Complete();
        Constants.HostInfo = host.Services.GetRequiredService<HostInfo>();
        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = host.Services.LogFor(typeof(WebMReader));

        step = trace.TrackStep("Starting host services");
        await host.Services.HostedServices().Start().ConfigureAwait(false);
        step.Complete();
        await host.RunAsync().ConfigureAwait(false);
    }

    public static async Task ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        string baseUrl)
    {
        // Logging
        services.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Debug)
            .AddFilter(null, LogLevel.Information) // Default level
            .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning)
            .AddFilter("Microsoft.AspNetCore.Authorization", LogLevel.Warning)
            .AddFilter("ActualChat", LogLevel.Debug)
            .AddFilter("ActualChat.Audio", LogLevel.Debug)
            .AddFilter("ActualChat.Audio.UI.Blazor", LogLevel.Debug)
            .AddFilter("ActualChat.Audio.UI.Blazor.Components", LogLevel.Debug)
            .AddFilter("ActualChat.Chat", LogLevel.Debug)
            .AddFilter("ActualChat.MediaPlayback", LogLevel.Debug)
            .AddFilter("ActualChat.Audio.Client", LogLevel.Debug)
        );

        // Other services shared with plugins
        services.TryAddSingleton(configuration);
        services.AddSingleton(c => new HostInfo() {
            AppKind = AppKind.WasmApp,
            Environment = c.GetService<IWebAssemblyHostEnvironment>()?.Environment ?? "Development",
            Configuration = c.GetRequiredService<IConfiguration>(),
            BaseUrl = baseUrl,
        });

        await AppStartup.ConfigureServices(services).ConfigureAwait(false);
    }
}
