using ActualChat.Audio;
using ActualChat.Module;
using ActualChat.Redis.Module;
using ActualChat.Streaming.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming.Module;

public sealed class StreamingServiceModule(IServiceProvider moduleServices)
    : HostModule<StreamingSettings>(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IAudioStreamingBackend>() is ServiceMode.Client;

        rpcHost.AddApi<ILiveAudioStreams, LiveAudioStreams>();
        rpcHost.AddApi<ILiveVideoStreams, LiveVideoStreams>();
        rpcHost.AddApi<ILiveSessions, LiveSessions>();
        rpcHost.AddBackend<IAudioStreamingBackend, AudioStreamingBackend>();
        rpcHost.AddBackend<IVideoStreamingBackend, VideoStreamingBackend>();
        rpcHost.AddBackend<ILiveAudioBackend, LiveAudioBackend>();
        rpcHost.AddBackend<ILiveVideoBackend, LiveVideoBackend>();
        rpcHost.AddBackend<ILiveSessionsBackend, LiveSessionsBackend>();
        services.AddSingleton<LiveStreamAccess>();
        services.AddSingleton<RemoteVideoStreamCache>();
        services.AddSingleton<RemoteAudioStreamCache>();
        services.TryAddSingleton<AudioSettings>(); // AudioSettings are not configured now
        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        services.AddSingleton(_ => new AudioSettings()); // Used in BlazorUIAppModule as well
        services.AddSingleton<AudioSegmentSaver>();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<StreamingContext>(services);
    }
}
