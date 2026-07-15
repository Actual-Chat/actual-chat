using ActualChat.Hosting;
using ActualChat.Performance;
using ActualChat.Testing.Internal;
using MartinCostello.Logging.XUnit;
using Microsoft.EntityFrameworkCore; // For EF Core log filters
using Microsoft.Extensions.Hosting;
using Xunit.DependencyInjection;
using Xunit.DependencyInjection.Logging;

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
            logging.AddFilter("ActualChat.Sharding", LogLevel.Debug);
            // logging.AddFilter("ActualLab.Fusion.EntityFramework", LogLevel.Debug);
            // logging.AddFilter("ActualLab.Fusion.EntityFramework.Operations", LogLevel.Debug);
            logging.AddTestLoggingProviders(outputAccessor);
        });
        return services;
    }

    public static ILoggerFactory CreateTestLoggerFactory(this TestOutputHelperAccessor outputAccessor)
        => new ServiceCollection()
            .AddTestLogging(outputAccessor)
            .BuildServiceProvider()
            .GetRequiredService<ILoggerFactory>();

    // Private methods

    private static void AddTestLoggingProviders(this ILoggingBuilder logging, TestOutputHelperAccessor outputAccessor)
    {
        logging.AddDebug();
        if (!TestRunnerInfo.IsBuildAgent()) {
            // Not "localhost": connects to ::1-mapped Docker ports may hang on Windows
            logging.AddSeq("http://127.0.0.1:5341");
        }

        // "Agent mode" - adds console logging so logs are visible in terminal
        // Useful for debugging tests via Claude Code or when running tests manually
        // Detected by presence of AC_OS environment variable (set by Claude Launcher)
        if (EnvExt.IsAgentMode())
            logging.AddSimpleConsole(options => {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss.fff ";
                options.IncludeScopes = false;
            });

        // XUnit logging requires weird setup b/c otherwise it filters out
        // everything below LogLevel.Information
 #pragma warning disable CS0618 // Type or member is obsolete
        logging.AddProvider(new XUnitLoggerProvider(
            new TestOutputHelperAdaptor(outputAccessor),
            new XUnitLoggerOptions() {
                Filter = (_, _) => true,
            }));
 #pragma warning restore CS0618 // Type or member is obsolete
    }
}
