using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.WebM;
using ActualChat.Hosting;
// ReSharper disable once RedundantUsingDirective
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.Diagnostics;
// Keep it: it lets <Project Sdk="Microsoft.NET.Sdk.Razor"> compile
using ActualLab.Internal;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
// ReSharper disable once RedundantUsingDirective
using Microsoft.Extensions.Configuration;
// Keep it: it lets <Project Sdk="Microsoft.NET.Sdk.Razor"> compile
using Tracer = ActualChat.Performance.Tracer;

namespace ActualChat.App.Wasm;

public static class Program
{
    [RequiresUnreferencedCode(UnreferencedCode.Reflection)]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WasmApp))]
    public static async Task Main(string[] args)
    {
        Tracer.Default.Point();

        ClientStartup.Initialize();
        AppUIOtelSetup.SetupConditionalPropagator();
        // NOTE(AY): This thing takes 1 second on Windows!
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<Microsoft.AspNetCore.Components.Web.HeadOutlet>("head::after");
        var baseUrl = builder.HostEnvironment.BaseAddress;
        var services = builder.Services;
        services.AddTracers(Tracer.Default, useScopedTracers: false);
        var hostInfo = Constants.HostInfo = ClientStartup.CreateHostInfo(
            builder.Configuration,
            builder.HostEnvironment.Environment,
            "Browser",
            HostKind.WasmApp,
            AppKind.Wasm,
            baseUrl);
        ClientStartup.ConfigureServices(services, hostInfo, null);
        var host = builder.Build();

        StaticLog.Factory = host.Services.LoggerFactory();
        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = host.Services.LogFor(typeof(WebMReader));

        await host.RunAsync().ConfigureAwait(false);
    }
}
