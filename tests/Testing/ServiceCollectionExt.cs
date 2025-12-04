using ActualChat.Hosting;
using ActualChat.Performance;
using ActualChat.Testing.Internal;
using MartinCostello.Logging.XUnit;
using Microsoft.EntityFrameworkCore; // For EF Core log filters
using Microsoft.Extensions.Hosting;
using Xunit.DependencyInjection;

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
        => AddTestLogging(services, new TestOutputHelperAccessor() { Output = output.ToSafe() });
    public static IServiceCollection AddTestLogging(this IServiceCollection services, TestOutputHelperAccessor outputAccessor)
    {
        services.AddTracers(c => c.LoggerFactory().NewTracer(), useScopedTracers: true);
        services.AddLogging(logging => {
            // Overriding default logging to more test-friendly setup
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
            if (!Constants.DebugMode.MeshLocks) {
                logging.AddFilter("ActualChat.Redis", LogLevel.Information);
                logging.AddFilter("ActualChat.Mesh", LogLevel.Information);
            }
            // logging.AddFilter("ActualLab.Fusion.EntityFramework", LogLevel.Debug);
            // logging.AddFilter("ActualLab.Fusion.EntityFramework.Operations", LogLevel.Debug);
            // logging.AddFilter(LogFilter);
            ConfigureTestLogging(logging, outputAccessor);
        });
        return services;
    }

    public static ILoggerFactory CreateTestLoggerFactory(this TestOutputHelperAccessor outputAccessor)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.ConfigureTestLogging(outputAccessor);
        });
        return services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
    }

    private static void ConfigureTestLogging(this ILoggingBuilder logging, TestOutputHelperAccessor outputAccessor)
    {
        logging.AddDebug();
        if (!TestRunnerInfo.IsBuildAgent())
            logging.AddSeq();
        // XUnit logging requires weird setup b/c otherwise it filters out
        // everything below LogLevel.Information
        logging.AddProvider(
#pragma warning disable CS0618
            new XUnitLoggerProvider(
                new TestOutputHelperAdaptor(outputAccessor),
                new XUnitLoggerOptions() {
                    Filter = (_, _) => true,
                    TimestampFormat = "HH:mm:ss.fff",
                }));
#pragma warning restore CS0618
    }
}
