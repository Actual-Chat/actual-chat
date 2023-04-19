using ActualChat.App.Maui.Services;
using ActualChat.Chat;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using IDispatcher = Microsoft.Maui.Dispatching.IDispatcher;

namespace ActualChat.App.Maui;

public static class MauiProgramOptimizations
{
    public static Task WarmupFusionServices(IServiceProvider services, Tracer tracer)
        => Task.Run(() => {
            var step = tracer.Region("WarmupFusionServices");
            try {
                var warmer = new ServiceWarmer(services);
                warmer.ReplicaService<IServerKvas>();
                warmer.ReplicaService<IAuth>();
                warmer.ReplicaService<IAccounts>();
                warmer.ReplicaService<IUserPresences>();
                warmer.ReplicaService<IChats>();

                warmer.ComputeService<ChatAudioUI>();
                warmer.ComputeService<AppPresenceReporterWorker>();
                warmer.ComputeService<ChatPlayers>();

                // After app rendered

                warmer.ComputeService<AccountUI>();
                warmer.ComputeService<ChatUI>();
                warmer.ReplicaService<ActualChat.Contacts.IContacts>();
                warmer.ReplicaService<IChatPositions>();
                warmer.ReplicaService<IMentions>();

                warmer.ReplicaService<IAuthors>();

                warmer.ComputeService<ClientFeatures>();
                warmer.ComputeService<ServerFeatures>();
                warmer.ReplicaService<IServerFeaturesClient>();

                warmer.ComputeService<SearchUI>();
                warmer.ReplicaService<IAvatars>();
                warmer.ComputeService<MediaPlayback.ActivePlaybackInfo>();
                warmer.ComputeService<LiveTime>();

                warmer.ComputeService<ChatRecordingActivity>();
                warmer.ReplicaService<IRoles>();
            }
            catch (Exception e) {
                tracer.Point("WarmupFusionServices failed, error: " + e);
            }
            finally {
                step.Close();
            }
        });

    public static void EnableDependencyInjectionEventListener()
        => _ = new Services.StartupTracing.DependencyInjectionEventListener();

    public static void AddDispatcherProxy(IServiceCollection services, bool logAllOperations)
    {
        var dispatcherDescriptor = services.FirstOrDefault(c => c.ServiceType == typeof(IDispatcher));
        if (dispatcherDescriptor?.ImplementationFactory == null)
            return;

        object ImplementationFactory(IServiceProvider svp)
            => new Services.StartupTracing.DispatcherProxy(
                (IDispatcher)dispatcherDescriptor.ImplementationFactory(svp),
                logAllOperations);

        services.Remove(dispatcherDescriptor);
        services.Add(new ServiceDescriptor(
            dispatcherDescriptor.ServiceType,
            ImplementationFactory,
            dispatcherDescriptor.Lifetime));
    }
}
