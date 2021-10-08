using System.Data;
using ActualChat.Audio.Db;
using ActualChat.Audio.Processing;
using ActualChat.Hosting;
using ActualChat.Redis;
using ActualChat.Transcription;
using ActualChat.Web.Module;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Stl.CommandR.Configuration;
using Stl.DependencyInjection;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Fusion.Operations.Internal;
using Stl.Plugins;

namespace ActualChat.Audio.Module;

public class AudioModule : HostModule<AudioSettings>, IWebModule
{
    public AudioModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public AudioModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        services.AddSingleton<IDbInitializer, AudioDbInitializer>();

        var fusion = services.AddFusion();
        services.AddDbContextFactory<AudioDbContext>(builder => {
            builder.UseNpgsql(Settings.Db);
            if (IsDevelopmentInstance)
                builder.EnableSensitiveDataLogging();
        });
        services.AddDbContextServices<AudioDbContext>(dbContext => {
            services.AddSingleton(new CompletionProducer.Options {
                IsLoggingEnabled = true,
            });
            services.AddTransient(c => new DbOperationScope<AudioDbContext>(c) {
                IsolationLevel = IsolationLevel.RepeatableRead,
            });
            dbContext.AddOperations((_, o) => {
                o.UnconditionalWakeUpPeriod = TimeSpan.FromSeconds(IsDevelopmentInstance ? 60 : 5);
            });
            dbContext.AddNpgsqlOperationLogChangeTracking();
        });
        services.AddCommander().AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<AudioDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<AudioDbContext>))
                return true;
            // 2. Make sure it's intact only for local commands
            var commandAssembly = commandType.Assembly;
            if (commandAssembly == typeof(AudioRecord).Assembly)
                return true;
            return false;
        });

        // Redis
        services.AddSingleton(_ => {
            var redis = ConnectionMultiplexer.Connect(Settings.Redis);
            return new RedisDb(redis);
        });

        // Module's own services
        services.AddSingleton<AudioSegmentSaver>();
        services.AddSingleton<AudioActivityExtractor>();
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
        services.AddSingleton<AudioStreamer>();
        services.AddTransient<IAudioStreamer>(c => c.GetRequiredService<AudioStreamer>());
        services.AddSingleton<AudioSourceStreamer>();
        services.AddTransient<IAudioSourceStreamer>(c => c.GetRequiredService<AudioSourceStreamer>());
        services.AddSingleton<TranscriptStreamer>();
        services.AddTransient<ITranscriptStreamer>(c => c.GetRequiredService<TranscriptStreamer>());
        services.AddSingleton<SourceAudioRecorder>();
        services.AddTransient<ISourceAudioRecorder>(c => c.GetRequiredService<SourceAudioRecorder>());
    }

    public void ConfigureApp(IApplicationBuilder app)
    {
        app.UseEndpoints(endpoints => {
            endpoints.MapHub<AudioHub>("/api/hub/audio");
        });
    }
}
