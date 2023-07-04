using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.Db;
using ActualChat.Audio.Processing;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using ActualChat.Transcription;
using ActualChat.Web.Module;
using Microsoft.AspNetCore.Builder;

namespace ActualChat.Audio.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class AudioServiceModule : HostModule<AudioSettings>, IWebModule
{
    public AudioServiceModule(IServiceProvider services) : base(services) { }

    public void ConfigureApp(IApplicationBuilder app)
        => app.UseEndpoints(endpoints => {
            endpoints.MapHub<AudioHub>("/api/hub/audio");
            endpoints.MapHub<AudioHubBackend>("/backend/hub/audio");
        });

    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.AppKind.IsServer())
            return; // Server-side only module

        services.AddResponseCaching();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<AudioContext>(services, Settings.Redis);

        // SignalR hub & related services
        var signalR = services.AddSignalR(options => {
            options.StreamBufferCapacity = 20;
            options.EnableDetailedErrors = true;
        });
        signalR.AddJsonProtocol();
        signalR.AddMessagePackProtocol();

        // Module's own services
        services.AddScoped<AudioHub>();
        services.AddSingleton<AudioHubBackend>();
        services.AddSingleton<AudioHubBackendClientFactory>();

        services.AddSingleton<AudioProcessor.Options>();
        services.AddSingleton<AudioProcessor>();
        services.AddTransient<IAudioProcessor>(c => c.GetRequiredService<AudioProcessor>());

        services.AddSingleton<AudioSegmentSaver>();
        services.AddSingleton<AudioDownloader, LocalAudioDownloader>();
        services.AddSingleton<AudioStreamer>();
        services.AddTransient<IAudioStreamer>(c => c.GetRequiredService<AudioStreamer>());
        services.AddSingleton<TranscriptStreamer>();
        services.AddTransient<ITranscriptStreamer>(c => c.GetRequiredService<TranscriptStreamer>());
        services.AddSingleton<AudioStreamServer>();
        services.AddSingleton<AudioStreamProxy>();
        services.AddTransient<IAudioStreamServer>(c => c.GetRequiredService<AudioStreamProxy>());
        services.AddSingleton<TranscriptStreamServer>();
        services.AddSingleton<TranscriptStreamProxy>();
        services.AddTransient<ITranscriptStreamServer>(c => c.GetRequiredService<TranscriptStreamProxy>());
    }
}
