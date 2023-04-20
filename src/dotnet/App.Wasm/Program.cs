using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.WebM;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
// ReSharper disable once RedundantUsingDirective
using ActualChat.UI.Blazor.App; // Keep it: it lets <Project Sdk="Microsoft.NET.Sdk.Razor"> compile
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
// ReSharper disable once RedundantUsingDirective
using Microsoft.Extensions.Configuration; // Keep it: it lets <Project Sdk="Microsoft.NET.Sdk.Razor"> compile
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sentry;
using Stl.CommandR.Interception;
using Stl.Fusion.Bridge.Interception;
using Stl.Fusion.Interception;
using Stl.Interception.Interceptors;
using Stl.Interception.Internal;

namespace ActualChat.App.Wasm;

public static class Program
{
    private static Tracer Tracer = null!;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WasmApp))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(InterfaceProxy))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TypeViewInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CommandServiceInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ComputeServiceInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ReplicaServiceInterceptor))]
    public static async Task Main(string[] args)
    {
        Tracer = Tracer.Default =
#if DEBUG || DEBUG_MAUI
            new Tracer("WasmApp", x => Console.WriteLine("@ " + x.Format()));
#else
            Tracer.None;
#endif
        Tracer.Point($"{nameof(Main)} started");

        // NOTE(AY): This thing takes 1 second on Windows!
        var isSentryEnabled = Constants.Sentry.EnabledFor.Contains(AppKind.MauiApp);
        var sentrySdkDisposable = isSentryEnabled
            ? SentrySdk.Init(options => options.ConfigureForApp())
            : null;
        try {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            var baseUrl = builder.HostEnvironment.BaseAddress;
            builder.Services.AddSingleton(Tracer);
            ConfigureServices(builder.Services, builder.Configuration, baseUrl);

            var region = Tracer.Region($"{nameof(WebAssemblyHostBuilder)}.Build");
            var host = builder.Build();
            region.Close();

            Constants.HostInfo = host.Services.GetRequiredService<HostInfo>();
            if (Constants.DebugMode.WebMReader)
                WebMReader.DebugLog = host.Services.LogFor(typeof(WebMReader));

            _ = StartHostedServices(host.Services);
            await host.RunAsync().ConfigureAwait(false);
        }
        catch (Exception exc) {
            if (!isSentryEnabled)
                throw;

            SentrySdk.CaptureException(exc);
            await SentrySdk.FlushAsync().ConfigureAwait(false);
            throw;
        }
        finally {
            sentrySdkDisposable.DisposeSilently();
        }
    }

    public static void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        string baseUrl)
    {
        using var _ = Tracer.Region(nameof(ConfigureServices));
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

        AppStartup.ConfigureServices(services, AppKind.WasmApp);
    }

    private static Task StartHostedServices(IServiceProvider services)
        => Task.Run(async () => {
            using var _ = Tracer.Region(nameof(StartHostedServices));
            await services.HostedServices().Start().ConfigureAwait(false);
        });
}
