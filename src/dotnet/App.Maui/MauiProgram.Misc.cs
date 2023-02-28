using ActualChat.Chat;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Kvas;
using ActualChat.Notification.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using IDispatcher = Microsoft.Maui.Dispatching.IDispatcher;

namespace ActualChat.App.Maui;

partial class MauiProgram
{
    private static Task WarmupFusionServices(IServiceProvider services)
        => Task.Run(() => {
            var step = _tracer.Region("WarmupFusionServices");
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

                warmer.ComputeService(typeof(RightPanelUI));
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
                _tracer.Point("WarmupFusionServices failed, error: " + e);
            }
            finally {
                step.Close();
            }
        });

    private static void AddDispatcherProxy(IServiceCollection services, bool logAllOperations)
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

    private static void EnableDependencyInjectionEventListener()
        => _ = new Services.StartupTracing.DependencyInjectionEventListener();
}
