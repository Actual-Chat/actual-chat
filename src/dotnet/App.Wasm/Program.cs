using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.WebM;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App; // Keep it: it lets <Project Sdk="Microsoft.NET.Sdk.Razor"> compile
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
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
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WasmApp))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(InterfaceProxy))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TypeViewInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CommandServiceInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ComputeServiceInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ReplicaServiceInterceptor))]
    public static async Task Main(string[] args)
    {
        var tracer = Tracer.Default =
#if DEBUG || DEBUG_MAUI
            new Tracer("WasmApp", x => Console.WriteLine("@ " + x.Format()));
#else
            Tracer.None;
#endif

        tracer.Point("Wasm.Program.Main");
        // Capture blazor bootstrapping errors

        // NOTE(AY): This thing takes 1 second on Windows!
        var isSentryEnabled = Constants.Sentry.EnabledFor.Contains(AppKind.MauiApp);
        var sentrySdkDisposable = isSentryEnabled
            ? SentrySdk.Init(options => options.ConfigureForApp())
            : null;
        try {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            var baseUrl = builder.HostEnvironment.BaseAddress;
            builder.Services.AddSingleton(tracer);
            var step = tracer.Region("ConfigureServices");
            await ConfigureServices(builder.Services, builder.Configuration, baseUrl).ConfigureAwait(false);
            step.Close();

            step = tracer.Region("Building wasm host");
            var host = builder.Build();
            step.Close();

            Constants.HostInfo = host.Services.GetRequiredService<HostInfo>();
            if (Constants.DebugMode.WebMReader)
                WebMReader.DebugLog = host.Services.LogFor(typeof(WebMReader));

            step = tracer.Region("Starting host services");
            await host.Services.HostedServices().Start().ConfigureAwait(false);
            step.Close();
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

        await AppStartup.ConfigureServices(services, AppKind.WasmApp).ConfigureAwait(false);
    }
}
