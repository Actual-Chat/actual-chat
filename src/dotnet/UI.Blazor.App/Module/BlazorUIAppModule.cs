using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Diagnostics;
using ActualChat.UI.Blazor.App.Services;
using Stl.Fusion.Bridge.Interception;
using Stl.Fusion.Diagnostics;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.App.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class BlazorUIAppModule : HostModule, IBlazorUIModule
{
    public static string ImportName => "blazorApp";

    public BlazorUIAppModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public BlazorUIAppModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
            return; // Blazor UI only module

        // Diagnostics
        var isDev = HostInfo.IsDevelopmentInstance;
        var isServer = HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server);
        var isWasm = HostInfo.RequiredServiceScopes.Contains(ServiceScope.WasmApp);

        if (isServer || (isDev && isWasm)) {
            if (Constants.Diagnostics.FusionMonitor)
                services.AddHostedService(c => {
                    return new FusionMonitor(c) {
                        SleepPeriod = isDev ? TimeSpan.Zero : TimeSpan.FromMinutes(5).ToRandom(0.2),
                        CollectPeriod = TimeSpan.FromSeconds(isDev ? 10 : 60),
                        AccessFilter = isWasm
                            ? static computed => computed.Input.Function is IReplicaMethodFunction
                            : static computed => true,
                        AccessStatisticsPreprocessor = StatisticsPreprocessor,
                        RegistrationStatisticsPreprocessor = StatisticsPreprocessor,
                    };

                    void StatisticsPreprocessor(Dictionary<string, (int, int)> stats)
                    {
                        if (isServer) {
                            foreach (var key in stats.Keys.ToList()) {
                                if (key.OrdinalStartsWith("DbAuthService"))
                                    continue;
                                if (key.OrdinalContains("Backend."))
                                    continue;
                                stats.Remove(key);
                            }
                        }
                        else {
                            foreach (var key in stats.Keys.ToList()) {
                                if (key.OrdinalContains(".Pseudo"))
                                    stats.Remove(key);
                                if (key.OrdinalStartsWith("FusionTime."))
                                    stats.Remove(key);
                                if (key.OrdinalStartsWith("LiveTime."))
                                    stats.Remove(key);
                                if (key.OrdinalStartsWith("LiveTimeDelta"))
                                    stats.Remove(key);
                            }
                        }
                    }
                });
        }
        if (isDev && isWasm) {
            if (Constants.Diagnostics.Wasm.TaskEventListener)
                services.AddHostedService(c => new TaskEventListener(c));
            if (Constants.Diagnostics.Wasm.TaskMonitor)
                services.AddHostedService(c => new TaskMonitor(c));
        }

        var isServerSideBlazor = HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server);
        if (!isServerSideBlazor) {
            services.AddScoped<SignOutReloader>(c => new SignOutReloader(c));
            services.ConfigureUILifetimeEvents(events => {
                events.OnAppInitialized += c => {
                    var signOutReloader = c.GetRequiredService<SignOutReloader>();
                    signOutReloader.Start();
                };
            });
        }

        var fusion = services.AddFusion();
        fusion.AddComputeService<AppPresenceReporter>(ServiceLifetime.Scoped);
        services.AddSingleton(_ => new AppPresenceReporter.Options {
            AwayTimeout = Constants.Presence.AwayTimeout,
        });
    }
}
