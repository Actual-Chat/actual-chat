using ActualChat.Hosting;
using ActualChat.Testing.Internal;
using ActualLab.Testing.Output;
using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Testing;

public static class ServiceCollectionExt
{
    public static IServiceCollection AddTestHostInfo(this IServiceCollection services)
        => services.AddTestHostInfo(out _);
    public static IServiceCollection AddTestHostInfo(this IServiceCollection services, out HostInfo hostInfo)
    {
        hostInfo = new HostInfo {
            HostKind = HostKind.Server,
            AppKind = AppKind.Unknown,
            Environment = Environments.Development,
            Roles = HostRoles.Server.GetAllRoles(HostRole.OneServer, true),
            IsTested = true,
        };
        services.AddSingleton(hostInfo);
        return services;
    }

    public static IServiceCollection AddTestLogging(this IServiceCollection services, ITestOutputHelper output)
        => AddTestLogging(services, new TestOutputHelperAccessor(output.ToSafe()));
    public static IServiceCollection AddTestLogging(this IServiceCollection services, TestOutputHelperAccessor outputAccessor)
        => services.AddLogging(logging => {
            // Overriding default logging to more test-friendly one
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Debug);
            // Set Constants.DebugMode.Npgsql to true, to enable Npgsql logging
            logging.AddFilter("Npgsql", LogLevel.Trace);
            // logging.AddFilter(DbLoggerCategory.Update.Name, LogLevel.Information);
            // logging.AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Information);
            // logging.AddFilter(DbLoggerCategory.Database.Transaction.Name, LogLevel.Debug);
            logging.AddFilter("ActualLab.CommandR", LogLevel.Information);
            logging.AddFilter("ActualLab.Fusion", LogLevel.Information);
            logging.AddFilter("ActualLab.Fusion.Diagnostics", LogLevel.Information);
            logging.AddFilter("ActualLab.Fusion.Operations", LogLevel.Information);
            logging.AddFilter("ActualChat.Redis", LogLevel.Information);
            logging.AddFilter("ActualChat.Mesh", LogLevel.Information);
            // logging.AddFilter("ActualLab.Fusion.EntityFramework", LogLevel.Debug);
            // logging.AddFilter("ActualLab.Fusion.EntityFramework.Operations", LogLevel.Debug);
            // logging.AddFilter(LogFilter);
            logging.AddDebug();
            // XUnit logging requires weird setup b/c otherwise it filters out
            // everything below LogLevel.Information
            logging.AddProvider(
#pragma warning disable CS0618
                new XUnitLoggerProvider(
                    new TestOutputHelperAccessorWrapper(outputAccessor),
                    new XUnitLoggerOptions() {
                        Filter = (_, _) => true,
                        TimestampFormat = "ss:fff",
                    }));
#pragma warning restore CS0618
        });
}
