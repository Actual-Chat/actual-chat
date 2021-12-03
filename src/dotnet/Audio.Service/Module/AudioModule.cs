using ActualChat.Audio.Db;
using ActualChat.Audio.Processing;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using ActualChat.Transcription;
using ActualChat.Web.Module;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Audio.Module;

public class AudioModule : HostModule<AudioSettings>, IWebModule
{
    public AudioModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public AudioModule(IPluginHost plugins) : base(plugins) { }

    public void ConfigureApp(IApplicationBuilder app)
        => app.UseEndpoints(endpoints => {
            endpoints.MapHub<AudioHub>("/api/hub/audio");
        });

    public override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        // Redis
        var redisModule = Plugins.GetPlugins<RedisModule>().Single();
        redisModule.AddRedisDb<AudioContext>(services, Settings.Redis);

        var fusion = services.AddFusion();


        services.AddResponseCaching();

        // Module's own services
        services.AddSingleton<AudioSegmentSaver>();
        services.AddSingleton<AudioActivityExtractor>();
        services.TryAddSingleton<SourceAudioProcessor.Options>();
        services.AddSingleton<SourceAudioProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<SourceAudioProcessor>());

        // SignalR hub & related services
        var signalR = services.AddSignalR(options => {
            options.StreamBufferCapacity = 20;
            options.EnableDetailedErrors = true;
        });
        if (!Debugging.SignalR.DisableMessagePackProtocol)
            signalR.AddMessagePackProtocol();
        services.AddTransient<AudioHub>();
        services.AddSingleton<AudioDownloader>();
        services.AddSingleton<AudioStreamer>();
        services.AddTransient<IAudioStreamer>(c => c.GetRequiredService<AudioStreamer>());
        services.AddSingleton<AudioSourceStreamer>();
        services.AddTransient<IAudioSourceStreamer>(c => c.GetRequiredService<AudioSourceStreamer>());
        services.AddSingleton<TranscriptStreamer>();
        services.AddTransient<ITranscriptStreamer>(c => c.GetRequiredService<TranscriptStreamer>());
        services.AddSingleton<SourceAudioRecorder>();
        services.AddTransient<ISourceAudioRecorder>(c => c.GetRequiredService<SourceAudioRecorder>());
    }
}
