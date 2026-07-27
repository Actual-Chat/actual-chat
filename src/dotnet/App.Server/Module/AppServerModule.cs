using System.IO.Compression;
using ActualChat.App.Server.Components.Pages;
using ActualChat.App.Server.Flows;
using ActualChat.App.Server.Health;
using ActualChat.AspNetCore;
using ActualChat.Db.Diagnostics;
using ActualChat.Diagnostics;
using ActualChat.Hosting;
using ActualChat.Kubernetes;
using ActualChat.Resilience;
using ActualChat.Resilience.Internal;
using ActualChat.Mcp;
using ActualChat.MLSearch.Diagnostics;
using ActualChat.Streaming.Diagnostics;
using ActualChat.Module;
using ActualChat.Redis;
using ActualChat.Redis.Module;
using ActualLab.Redis;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.CommandR.Diagnostics;
using ActualLab.Fusion.Diagnostics;
using ActualLab.Fusion.EntityFramework.Internal;
using ActualChat.Authentication;
using ActualLab.Fusion.Server;
using ActualLab.IO;
using ActualLab.Rpc.Diagnostics;
using ActualLab.Rpc.Server;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ActualChat.App.Server.Module;

/// <summary>
/// Main server module configuring web hosting, Blazor, OpenTelemetry, and infrastructure services.
/// </summary>
public sealed class AppServerModule(IServiceProvider moduleServices)
    : HostModule<HostSettings>(moduleServices), IWebServerModule
{
    public IWebHostEnvironment Env => field ??= ModuleServices.GetRequiredService<IWebHostEnvironment>();

    public void ConfigureApp(WebApplication app)
    {
        if (Settings.AssumeHttps) {
            Log.LogWarning("AssumeHttps is on");
            app.Use((context, next) => {
                context.Request.Scheme = "https";
                return next();
            });
        }

        // This server serves static content from Blazor Client,
        // and since we don't copy it to local wwwroot,
        // we need to find Client's wwwroot in bin/(Debug/Release) folder
        // and set it as this server's content root.
        /*
        Env.WebRootPath =  Settings.WebRootPath.NullIfEmpty() ?? AppPathResolver.GetWebRootPath();
        Env.ContentRootPath = AppPathResolver.GetContentRootPath();
        Env.WebRootFileProvider = new PhysicalFileProvider(Env.WebRootPath);
        Env.ContentRootFileProvider = new PhysicalFileProvider(Env.ContentRootPath);
        StaticWebAssetsLoader.UseStaticWebAssets(Env, Cfg);
        */

        if (Env.IsDevelopment())
            app.UseWebAssemblyDebugging();
        else {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        // See
        // - https://docs.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins?view=aspnetcore-6.0
        // - https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-6.0
        app.UseForwardedHeaders();
        app.UseResponseHeaders();

        // And here we can modify httpContext.Request.Scheme & Host manually to whatever we like
        var baseUrl = Settings.BaseUri;
        if (!baseUrl.IsNullOrEmpty()) {
            Log.LogInformation("Overriding request host to {BaseUrl}", baseUrl);
            app.UseBaseUrl(baseUrl);
        }

        app.UseWebSockets(new WebSocketOptions {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });

        // Explicit rewrite cause files without extension (hence no content-type) are not served due to security reasons
        app.UseRewriter(
            new RewriteOptions().AddRewrite("\\.well-known/apple-app-site-association$",
                ".well-known/apple-app-site-association.json",
                true));

        // Response compression
        if (!Env.IsDevelopment()) // disable compression for local development and hot reload
            app.UseResponseCompression();

        // Cache headers for static files (must be before UseStaticFiles)
        app.UseStaticDistCacheHeaders();

        // Serve /dist/assets with custom MIME types for .ort, .onnx, etc.
        // MUST be BEFORE UseRouting() so it runs before endpoint routing
        // These files are not in the MapStaticAssets manifest (build-time warning expected)
        if (!HostInfo.IsTested) {
            var webRootPath = AppPathResolver.GetWebRootPath();
            Log.LogInformation("WebRootPath resolved to: {WebRootPath}", webRootPath.Value);

            var assetsPath = webRootPath & "dist/assets";
            var assetsPathExists = Directory.Exists(assetsPath);
            Log.LogInformation("AssetsPath: {AssetsPath}, exists: {Exists}", assetsPath.Value, assetsPathExists);
            if (assetsPathExists)
                app.UseStaticFiles(new StaticFileOptions {
                    RequestPath = "/dist/assets",
                    FileProvider = new PhysicalFileProvider(assetsPath),
                    ContentTypeProvider = ContentTypeProvider.Instance,
                });
        }

        // API controllers & HTTP endpoints
        app.UseRouting();
        app.UseCors("Default");
        app.UseResponseCaching();
        app.UseMiddleware<HttpRateLimitMiddleware>();
        if (HostInfo.HasRole(HostRole.Api))
            app.UseAuthentication();
        app.UseAntiforgery();

        // Endpoint mapping
        if (!HostInfo.IsTested) {
            var webRootPath = AppPathResolver.GetWebRootPath();

            var configPath = webRootPath & "dist/config";
            if (Directory.Exists(configPath))
                app.UseStaticFiles(new StaticFileOptions {
                    RequestPath = "/dist/config", // For firebase.config.js
                    FileProvider = new PhysicalFileProvider(configPath),
                });

            app.MapStaticAssets();
            app.MapRazorComponents<RootServerPage>()
                .AddInteractiveServerRenderMode()
                .AddInteractiveWebAssemblyRenderMode()
                .AddAdditionalAssemblies(typeof(WebApp).Assembly) // UI.Blazor.AppPack
                .AddAdditionalAssemblies(typeof(UIHub).Assembly) // UI.Blazor
                .AddAdditionalAssemblies(typeof(AppUIHub).Assembly); // UI.Blazor.App
        }
        app.MapRpcWebSocketServer();
        app.MapRpcHttpServer();
        app.MapGet("/rpc/check", () => Results.Text("ok"));
        if (HostInfo.HasRole(HostRole.Api)) {
            app.MapFusionAuthEndpoints(); // /signIn, /signOut
            app.MapFusionRenderModeEndpoints(); // /fusion/renderMode
        }
        app.MapControllers();
        app.MapMcp();

        // Diagnostic endpoints
        // app.UseOpenTelemetryPrometheusScrapingEndpoint();
        app.MapAppHealth();
        // Disabled as we disabled prometheus endpoint recently
        // endpoints.MapAppMetrics();
    }

    protected override void InjectServices(IServiceCollection services)
    {
        // Host options
        services.Configure<HostOptions>(o => {
            o.ShutdownTimeout = Env.IsDevelopment()
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromSeconds(30);
        });

        // AppHostLifecycleMonitor
        services.AddSingleton(c => new AppHostLifecycleMonitor(c));
        services.AddHostedService(c => c.GetRequiredService<AppHostLifecycleMonitor>());

        // Health-checks
        services.AddSingleton<LivelinessHealthCheck>(c => new LivelinessHealthCheck());
        services.AddSingleton<ReadinessHealthCheck>(c => new ReadinessHealthCheck(c));
        services.AddHealthChecks()
            .AddCheck<LivelinessHealthCheck>("App-Liveliness", tags: [HealthTags.Live])
            .AddCheck<ReadinessHealthCheck>("App-Readiness", tags: [HealthTags.Ready]);

        // Aspire-related
        if (Settings.IsAspireManaged) {
            services.AddServiceDiscovery();
            services.ConfigureHttpClientDefaults(http => {
                // http.AddStandardResilienceHandler();
                http.AddServiceDiscovery();
            });
            services.AddHealthChecks()
                // Add a default liveness check to ensure app is responsive
                .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        }

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<InfrastructureDbContext>(services);

        // Inbound call budgets
        services.AddSingleton(_ => RateLimitBudgets.Default);
        services.AddSingleton(_ => RateLimitClassResolver.Default);
        services.AddSingleton(c => new RateLimitIdentityResolver(c));
        services.AddSingleton(c => RedisRateLimitPolicy.New(
            c.GetRequiredService<RedisDb<InfrastructureDbContext>>(),
            c.GetRequiredService<RateLimitBudgets>(),
            c));
        services.AddSingleton<RateLimitUserIdResolver>(c => {
            var sessionsBackend = c.GetRequiredService<ISessionsBackend>();
            return async (session, cancellationToken) => {
                var sessionInfo = await sessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
                return sessionInfo?.UserId;
            };
        });

        // Mesh Locks
        var hasKube = IKubeInfo.HasKube();
        var useLocalKube = Constants.DebugMode.KubeLocal;
        if (useLocalKube)
            // Validate that we are not managed by k8s and register local kube info otherwise
            if (!hasKube)
                services.AddSingleton(KubeInfo.GetLocal);

        var renewerThreadCount = Settings.MeshLockRenewerThreadCount;
        if (renewerThreadCount < 1)
            renewerThreadCount = Math.Max(2, Environment.ProcessorCount / 4);
        Log.LogInformation("MeshLockRenewer: using {ThreadCount} thread(s)", renewerThreadCount);
        services.AddSingleton(new MeshLockRenewer(renewerThreadCount));

        services.AddSingleton<IMeshLocks>(c => {
            var subspace = Settings.MeshLockSubspace;
            if (subspace == "?")
                subspace = Alphabet.AlphaNumeric.Generator8.Next();
            else if (subspace.IsNullOrWhiteSpace())
                subspace = "";
            var optionsPreset = Settings.MeshLockOptionsPreset.NullIfEmpty() ?? nameof(MeshLockOptions.Default);
            Log.LogInformation("IMeshLocks: '{Subspace}' subspace, '{OptionsPreset}' lock options preset", subspace, optionsPreset);

            var keyPrefix = subspace.IsNullOrEmpty()
                ? MeshLocksBase.DefaultKeyPrefix
                : $"{MeshLocksBase.DefaultKeyPrefix}-{subspace}"; // Must not use "." as a delimiter!

            if (useLocalKube) {
                Log.LogInformation("Using {KubeMeshLocks} for mesh locks", useLocalKube ? "Local KubeMeshLocks" : "KubeMeshLocks");
                return c.GetRequiredService<KubeMeshLocks>()
                    .With(keyPrefix, MeshLockOptions.Presets[optionsPreset]);
            }

            Log.LogInformation("Using RedisMeshLocks for mesh locks");
            return new RedisMeshLocks<InfrastructureDbContext>(c, keyPrefix) {
                LockOptions = MeshLockOptions.Presets[optionsPreset],
            };
        });

        // Flows
        services.AddFlows()
            .Add<MigrationFlow>()
            .Add<IconSvgToPngMigrationFlow>();

        // Web
        var binPath = new FilePath(Assembly.GetExecutingAssembly().Location).FullPath.DirectoryPath;
        var dataProtection = Settings.DataProtection.NullIfEmpty() ?? binPath & "data-protection-keys";
        Log.LogInformation("DataProtection path: {DataProtection}", dataProtection);
        if (dataProtection.StartsWith("gs://")) {
            var bucket = dataProtection[5..dataProtection.IndexOf('/', 5)];
            var objectName = dataProtection[(6 + bucket.Length)..];
            services.AddDataProtection().PersistKeysToGoogleCloudStorage(bucket, objectName);
        }
        else
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtection));
        services.AddSingleton(new ContentSecurityPolicy(HostInfo));
        var origins = new List<string> {
            "http://0.0.0.0",
            "http://0.0.0.1",
            "https://0.0.0.0",
            "https://0.0.0.1",
            "app://0.0.0.0",
            "app://0.0.0.1",
            "http://localhost",
            "https://localhost",
            "app://localhost",
        };
        if (Env.IsDevelopment()) {
            origins.AddRange(Constants.Hosts.AllDev.Select(host => $"https://{host}"));
            origins.AddRange(Constants.Hosts.AllLocal.Select(host => $"https://{host}"));
        }
        else if (Env.IsStaging())
            origins.AddRange(Constants.Hosts.AllDev.Select(host => $"https://{host}"));
        else
            origins.AddRange(Constants.Hosts.AllProd.Select(host => $"https://{host}"));

        services.AddCors(options => {
            options.AddPolicy("Default", builder => {
                builder.WithOrigins(origins.ToArray())
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
            options.AddPolicy("CDN", builder => {
                builder
                    .WithOrigins(origins.ToArray())
                    .WithMethods("GET")
                    .AllowAnyHeader()
                    .WithExposedHeaders("Content-Encoding","Content-Length","Content-Range", "Content-Type");
            });
        });
        // services.Configure<HstsOptions>(options => {
        // options.ExcludedHosts.Add(Constants.Hosts.LocalVoxt);
        //     options.ExcludedHosts.Add("localhost");
        // });
        services.Configure<ForwardedHeadersOptions>(options => {
            options.ForwardedHeaders = ForwardedHeaders.All;
            if (Settings.AssumeHttps)
                options.ForwardedHeaders &= ~ForwardedHeaders.XForwardedProto;
            options.SetKnownProxies(Settings.KnownProxyNetworks, Settings.KnownProxies, Log);
            Log.LogInformation("ForwardedHeaders: {NetworkCount} known network(s), {ProxyCount} known proxy(ies)",
                options.KnownIPNetworks.Count, options.KnownProxies.Count);
        });

        // Compression
        if (!Env.IsDevelopment()) { // Disable compression for local development and hot reload
            services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
            services.AddResponseCompression(o => {
                o.EnableForHttps = true;
                o.Providers.Add<BrotliCompressionProvider>();
            });
        }

        // Blazor Server
        services.AddServerSideBlazor(o => {
            if (HostInfo.IsDevelopmentInstance) {
                o.DisconnectedCircuitMaxRetained = 5;
                o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(1);
            }
            else { // Production
                o.DisconnectedCircuitMaxRetained = 100; // Default is 100
                o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(2); // Default is 3 min.
            }
            o.MaxBufferedUnacknowledgedRenderBatches = 100; // Default is 10, chat switch needs more
            o.DetailedErrors = HostInfo.IsDevelopmentInstance;
        }).AddHubOptions(o => {
            o.MaximumParallelInvocationsPerClient = 4;
            o.StatefulReconnectBufferSize = 1000;
        });
        services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .AddInteractiveWebAssemblyComponents();
        services.AddBlazorCircuitActivitySuppressor();

        // Open Telemetry
        if (!HostInfo.IsTested)
            ConfigureOpenTelemetry(services);
    }

    // Private methods

    private void ConfigureOpenTelemetry(IServiceCollection services)
    {
        var serviceName = Cfg["OTEL_SERVICE_NAME"].NullIfEmpty() ?? "App";
        var endpointUrl = Cfg["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (endpointUrl.IsNullOrEmpty()) {
            var endpoint = Settings.OpenTelemetryEndpoint.NullIfEmpty() ?? "localhost";
            var (host, port) = endpoint.ParseHostPort(4317);
            endpointUrl = $"http://{host}:{port.Format()}";
        }

        Log.LogInformation("OpenTelemetry: '{ServiceName}' @ {Endpoint}", serviceName, endpointUrl);

        var otel = services.AddOpenTelemetry();
        otel.ConfigureResource(resource => {
            _ = Env.IsDevelopment()
                ? resource.AddService(serviceName, "actualchat", ApiConstants.FullVersionString, false, "dev")
                : resource.AddService(serviceName, "actualchat", ApiConstants.FullVersionString);

            var containerName = Environment.GetEnvironmentVariable("CONTAINER_NAME").NullIfEmpty() ?? "actual-chat-app";
            if (!string.IsNullOrEmpty(containerName))
                resource.AddAttributes([KeyValuePair.Create<string, object>("k8s.container.name", containerName)]);
        });

        otel.WithMetrics(meter => meter
            // gcloud exporter doesn't support some of metrics yet:
            // - https://github.com/open-telemetry/opentelemetry-collector-contrib/discussions/2948
            .AddProcessInstrumentation()
            .AddMeter(RpcInstruments.Meter.Name) // ActualLab.Rpc
            .AddMeter(CommanderInstruments.Meter.Name) // ActualLab.Commander
            .AddMeter(FusionInstruments.Meter.Name) // ActualLab.Fusion
            .AddMeter(FusionEntityFrameworkInstruments.Meter.Name) // ActualLab.Fusion.EntityFramework
            // Our own meters (one per assembly)
            .AddMeter(DbInstruments.Meter.Name)
            .AddMeter(CoreServerInstruments.Meter.Name)
            .AddMeter(AppInstruments.Meter.Name)
            .AddMeter(AppUIInstruments.Meter.Name)
            .AddMeter(MLSearchInstruments.Meter.Name)
            .AddMeter(StreamingInstruments.Meter.Name)
            .AddOtlpExporter(cfg => {
                cfg.ExportProcessorType = ExportProcessorType.Batch;
                cfg.BatchExportProcessorOptions = new BatchExportActivityProcessorOptions {
                    ExporterTimeoutMilliseconds = 10_000,
                    MaxExportBatchSize = 200, // Google Cloud Monitoring limits batches to 200 metric points.
                    MaxQueueSize = 1024,
                    ScheduledDelayMilliseconds = 5_000,
                };
                cfg.Protocol = OtlpExportProtocol.Grpc;
                cfg.Endpoint = endpointUrl.ToUri();
            })
        );
        otel.WithTracing(tracer => tracer
            .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(
                Settings.OpenTelemetryTraceSampleRate)))
            .SetErrorStatusOnException()
            .AddSource(RpcInstruments.ActivitySource.Name) // ActualLab.Rpc
            .AddSource(CommanderInstruments.ActivitySource.Name) // ActualLab.Commander
            .AddSource(FusionInstruments.ActivitySource.Name) // ActualLab.Fusion
            .AddSource(FusionEntityFrameworkInstruments.ActivitySource.Name) // ActualLab.Fusion.EntityFramework
            // Our own activity sources (one per assembly)
            .AddSource(DbInstruments.ActivitySource.Name)
            .AddSource(CoreServerInstruments.ActivitySource.Name)
            .AddSource(AppInstruments.ActivitySource.Name)
            .AddSource(AppUIInstruments.ActivitySource.Name)
            .AddSource(MLSearchInstruments.ActivitySource.Name)
            .AddSource(StreamingInstruments.ActivitySource.Name)
            .AddAspNetCoreInstrumentation(opt => {
                var excludedPaths = new PathString[] {
                    "/favicon_voxt.ico",
                    "/metrics",
                    "/status",
                    "/_blazor",
                    "/_framework",
                    "/healthz",
                    "/api/hub/streams",
                    "/rpc/ws",
                    "/backend/rpc/ws",
                    "/rpc/http",
                    "/backend/rpc/http",
                };
                opt.Filter = httpContext =>
                    !excludedPaths.Any(x
                        => httpContext.Request.Path.StartsWithSegments(x, StringComparison.OrdinalIgnoreCase));
            })
            .AddHttpClientInstrumentation(cfg => cfg.RecordException = true)
            .AddGrpcClientInstrumentation()
            .AddNpgsql()
            .AddOtlpExporter(cfg => {
                cfg.ExportProcessorType = ExportProcessorType.Batch;
                cfg.BatchExportProcessorOptions = new BatchExportActivityProcessorOptions() {
                    ExporterTimeoutMilliseconds = 10_000,
                    MaxExportBatchSize = 200, // Google Cloud Monitoring limits batches to 200 metric points.
                    MaxQueueSize = 1024,
                    ScheduledDelayMilliseconds = 5_000,
                };
                cfg.Protocol = OtlpExportProtocol.Grpc;
                cfg.Endpoint = endpointUrl.ToUri();
            })
        );
        otel.WithLogging(
            logger => logger.AddOtlpExporter(cfg => {
                cfg.ExportProcessorType = ExportProcessorType.Batch;
                cfg.BatchExportProcessorOptions = new BatchExportActivityProcessorOptions() {
                    ExporterTimeoutMilliseconds = 10_000,
                    MaxExportBatchSize = 200, // Google Cloud Monitoring limits batches to 200 metric points.
                    MaxQueueSize = 1024,
                    ScheduledDelayMilliseconds = 5_000,
                };
                cfg.Protocol = OtlpExportProtocol.Grpc;
                cfg.Endpoint = endpointUrl.ToUri();
            }),
            options => {
                options.IncludeScopes = true;
                options.IncludeFormattedMessage = true;
            });
    }
}
