using ActualChat.App.Maui.Services;
using ActualChat.Chat;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Kvas;
using ActualChat.Notification.UI.Blazor;
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

                // after app rendered

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
                warmer.ComputeService<NotificationUI>();

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
        var dispatcherRegistration =
            services.FirstOrDefault(c => c.ServiceType == typeof(IDispatcher));
        if (dispatcherRegistration == null || dispatcherRegistration.ImplementationFactory == null)
            return;

        services.Remove(dispatcherRegistration);
        Func<IServiceProvider, object> implementationFactory = svp =>
            new Services.StartupTracing.DispatcherProxy((IDispatcher) dispatcherRegistration.ImplementationFactory(svp), logAllOperations);
        var serviceDescriptor = new ServiceDescriptor(
            dispatcherRegistration.ServiceType,
            implementationFactory,
            dispatcherRegistration.Lifetime);
        services.Add(serviceDescriptor);
    }
}
