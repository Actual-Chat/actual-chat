using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.WebM;
using ActualChat.Diff.Handlers;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
// ReSharper disable once RedundantUsingDirective
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Services; // Keep it: it lets <Project Sdk="Microsoft.NET.Sdk.Razor"> compile
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
// ReSharper disable once RedundantUsingDirective
using Microsoft.Extensions.Configuration; // Keep it: it lets <Project Sdk="Microsoft.NET.Sdk.Razor"> compile
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Trace;
using Sentry;
using Stl.CommandR.Interception;
using Stl.Fusion.Client.Interception;
using Stl.Fusion.Client.Internal;
using Stl.Fusion.Interception;
using Stl.Interception;
using Stl.Interception.Interceptors;
using Stl.Interception.Internal;
using Stl.Rpc;
using Stl.Rpc.Infrastructure;
using Tracer = ActualChat.Performance.Tracer;

namespace ActualChat.App.Wasm;

public static class Program
{
    private static Tracer Tracer { get; set; } = Tracer.None;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WasmApp))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(InterfaceProxy))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TypeViewInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TypedFactoryInterceptor))]
    // Stl.Rpc
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcClientInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcRoutingInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInboundContext))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInboundContextFactory))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcOutboundContext))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInboundCall<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcOutboundCall<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Result<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ResultBox<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,,,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,,,,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,,,,,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,,,,,,,>))]
    // CommandR
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CommandContext<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MethodCommandHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(InterfaceCommandHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CommandServiceInterceptor))]
    // Fusion
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ComputeMethodFunction<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInboundComputeCall<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcOutboundComputeCall<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ComputeServiceInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ClientComputeServiceInterceptor))]
    // Diffs
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MissingDiffHandler<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CloneDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NullableDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RecordDiffHandler<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(OptionDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SetDiffHandler<,>))]
    public static async Task Main(string[] args)
    {
#if DEBUG
        Tracer = Tracer.Default = new Tracer("WasmApp", x => Console.WriteLine("@ " + x.Format()));
#endif
        Tracer.Point($"{nameof(Main)} started");
        OtelDiagnostics.SetupConditionalPropagator();

        FusionSettings.Mode = FusionMode.Client;

        // NOTE(AY): This thing takes 1 second on Windows!
        var isSentryEnabled = Constants.Sentry.EnabledFor.Contains(AppKind.MauiApp);
        var sentrySdkDisposable = isSentryEnabled
            ? SentrySdk.Init(options => options.ConfigureForApp(true))
            : null;
        IDisposable? traceProvider = null;
        try {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<Microsoft.AspNetCore.Components.Web.HeadOutlet>("head::after");
            var baseUrl = builder.HostEnvironment.BaseAddress;
            builder.Services.AddTracer(Tracer);
            ConfigureServices(builder.Services, builder.Configuration, baseUrl);

            var region = Tracer.Region($"{nameof(WebAssemblyHostBuilder)}.Build");
            var host = builder.Build();
            region.Close();

            Constants.HostInfo = host.Services.GetRequiredService<HostInfo>();
            if (Constants.DebugMode.WebMReader)
                WebMReader.DebugLog = host.Services.LogFor(typeof(WebMReader));
            if (sentrySdkDisposable != null)
                CreateClientSentryTraceProvider(host.Services, c => traceProvider = c);

            await host.RunAsync().ConfigureAwait(false);
        }
        catch (Exception e) {
            if (!isSentryEnabled)
                throw;

            SentrySdk.CaptureException(e);
            await SentrySdk.FlushAsync().ConfigureAwait(false);
            throw;
        }
        finally {
            traceProvider.DisposeSilently();
            sentrySdkDisposable.DisposeSilently();
        }
    }

    public static void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        string baseUrl,
        bool isTested = false)
    {
        using var _ = Tracer.Region();
        services.AddSingleton(new ScopedTracerProvider(Tracer)); // We don't want to have scoped tracers in WASM app

        // Logging
        services.AddLogging(logging => logging.ConfigureClientFilters(ClientKind.Wasm));

        // Other services shared with plugins
        services.TryAddSingleton(configuration);
        services.AddSingleton(c => new HostInfo() {
            AppKind = AppKind.WasmApp,
            ClientKind = ClientKind.Wasm,
            IsTested = isTested,
            Environment = c.GetService<IWebAssemblyHostEnvironment>()?.Environment ?? "Development",
            Configuration = c.GetRequiredService<IConfiguration>(),
            BaseUrl = baseUrl,
        });

        AppStartup.ConfigureServices(services, AppKind.WasmApp);
    }

    private static void CreateClientSentryTraceProvider(IServiceProvider services, Action<TracerProvider?> saveTracerProvider)
    {
        var urlMapper = services.GetRequiredService<UrlMapper>();
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
            saveTracerProvider(OtelDiagnostics.CreateClientSentryTraceProvider("WasmApp"));
        }
    }
}
