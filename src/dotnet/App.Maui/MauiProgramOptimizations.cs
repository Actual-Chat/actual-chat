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
                var warmer = new Services.StartupTracing.FusionServicesWarmer(services);
                warmer.ReplicaService<IServerKvas>();
                warmer.ReplicaService<IAuth>();
                warmer.ReplicaService<IAccounts>();
                warmer.ReplicaService<IUserPresences>();
                warmer.ReplicaService<IChats>();

                warmer.ComputeService(typeof(ChatAudioUI));
                warmer.ComputeService(typeof(AppPresenceReporter.Worker));
                warmer.ComputeService(typeof(ChatPlayers));
                warmer.ComputeService(typeof(ActiveChatsUI));

                // after app rendered

                warmer.ComputeService(typeof(AccountUI));
                warmer.ComputeService(typeof(ChatUI));
                warmer.ReplicaService<ActualChat.Contacts.IContacts>();
                warmer.ReplicaService<IChatPositions>();
                warmer.ReplicaService<IMentions>();

                warmer.ReplicaService<IAuthors>();

                warmer.ComputeService(typeof(ClientFeatures));
                warmer.ComputeService(typeof(ServerFeatures));
                warmer.ReplicaService<ServerFeaturesClient.IClient>();

                warmer.ComputeService(typeof(SearchUI));
                warmer.ReplicaService<IAvatars>();
                warmer.ComputeService(typeof(MediaPlayback.ActivePlaybackInfo));
                warmer.ComputeService(typeof(LiveTime));

                warmer.ComputeService(typeof(ChatRecordingActivity));
                warmer.ComputeService(typeof(NotificationUI));

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
