using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.WebM;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.UI.Blazor;
// ReSharper disable once RedundantUsingDirective
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Services; // Keep it: it lets <Project Sdk="Microsoft.NET.Sdk.Razor"> compile
using ActualLab.Internal;
using ActualLab.Rpc;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
// ReSharper disable once RedundantUsingDirective
using Microsoft.Extensions.Configuration;
// Keep it: it lets <Project Sdk="Microsoft.NET.Sdk.Razor"> compile
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Trace;
using Tracer = ActualChat.Performance.Tracer;

namespace ActualChat.App.Wasm;

public static class Program
{
    [RequiresUnreferencedCode(UnreferencedCode.Reflection)]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WasmApp))]
    public static async Task Main(string[] args)
    {
        Tracer.Default.Point();

        ClientAppStartup.Initialize();
        AppUIOtelSetup.SetupConditionalPropagator();
        // NOTE(AY): This thing takes 1 second on Windows!
        var isSentryEnabled = Constants.Sentry.EnabledFor.Contains(HostKind.WasmApp);
        var sentrySdkDisposable = (IDisposable?)null; // isSentryEnabled
            // ? SentrySdk.Init(options => options.ConfigureForApp(true))
            // : null;
        IDisposable? traceProvider = null;
        try {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<Microsoft.AspNetCore.Components.Web.HeadOutlet>("head::after");
            var baseUrl = builder.HostEnvironment.BaseAddress;
            var services = builder.Services;
            services.AddTracers(Tracer.Default, useScopedTracers: false);
            var hostInfo = Constants.HostInfo = ClientAppStartup.CreateHostInfo(
                builder.Configuration,
                builder.HostEnvironment.Environment,
                "Browser",
                HostKind.WasmApp,
                AppKind.Wasm,
                baseUrl);
            ClientAppStartup.ConfigureServices(services, hostInfo);
            var host = builder.Build();

            StaticLog.Factory = host.Services.LoggerFactory();
            if (Constants.DebugMode.WebMReader)
                WebMReader.DebugLog = host.Services.LogFor(typeof(WebMReader));
            if (sentrySdkDisposable != null)
                CreateClientSentryTraceProvider(host.Services, c => traceProvider = c);

            await host.RunAsync().ConfigureAwait(false);
        }
        catch (Exception) {
            if (!isSentryEnabled)
                throw;

            // SentrySdk.CaptureException(e);
            // await SentrySdk.FlushAsync().ConfigureAwait(false);
            throw;
        }
        finally {
            traceProvider.DisposeSilently();
            sentrySdkDisposable.DisposeSilently();
        }
    }

    private static void CreateClientSentryTraceProvider(IServiceProvider services, Action<TracerProvider?> saveTracerProvider)
    {
        var urlMapper = services.UrlMapper();
        if (!urlMapper.IsActualChat) {
            CreateAndSaveTracerProvider();
            return;
        }

        _ = BackgroundTask.Run(async () => {
            try {
                var accountUI = services.GetRequiredService<AccountUI>();
                await accountUI.WhenLoaded.ConfigureAwait(false);
                var ownAccount = await accountUI.OwnAccount.Use().ConfigureAwait(false);
                if (!ownAccount.IsAdmin)
                    return;

                CreateAndSaveTracerProvider();
            }
            catch (Exception e) {
                await Console.Error.WriteLineAsync("Failed to access AccountUI: " + e)
                    .ConfigureAwait(false);
            }
        });

        void  CreateAndSaveTracerProvider() {
            saveTracerProvider(AppUIOtelSetup.CreateClientSentryTraceProvider("WasmApp"));
        }
    }
}
