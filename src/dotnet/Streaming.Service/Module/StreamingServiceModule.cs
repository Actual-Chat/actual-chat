using ActualChat.Audio;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Redis.Module;
using ActualChat.Streaming.Services;
using Microsoft.AspNetCore.Builder;
using LocalAudioDownloader = ActualChat.Streaming.Services.LocalAudioDownloader;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming.Module;

public sealed class StreamingServiceModule(IServiceProvider moduleServices)
    : HostModule<AudioSettings>(moduleServices), IWebServerModule
{
    public void ConfigureApp(IApplicationBuilder app)
        => app.UseEndpoints(endpoints => {
            endpoints.MapHub<StreamHub>("/api/hub/streams");
            endpoints.MapHub<StreamHub>("/api/hub/audio"); // For backward compatibility!
        });

    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.HostKind.IsServer())
            return; // Server-side only module

        // Backend
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IStreamingBackend>().IsClient();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<StreamingContext>(services, Settings.Redis);

        // SignalR hub & related services
        if (HostInfo.HasRole(HostRole.FrontendServer)) {
            var signalR = services.AddSignalR(options => {
                options.StreamBufferCapacity = 20;
                options.EnableDetailedErrors = false;
            });
            signalR.AddJsonProtocol();
            signalR.AddMessagePackProtocol();
            services.AddScoped<StreamHub>();
        }

        // Module's own services
        if (!isBackendClient) {
            services.AddSingleton<AudioSegmentSaver>();
            services.AddSingleton<StreamingBackend.Options>();
        }
        rpcHost.AddFrontend<IStreamServer, StreamServer>();
        rpcHost.AddBackend<IStreamingBackend, StreamingBackend>();
        services.AddSingleton<IStreamClient, StreamBackendClient>();
        services.AddSingleton<AudioDownloader, LocalAudioDownloader>();
    }
}
