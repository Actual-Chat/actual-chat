using ActualLab.Testing.Output;
using Xunit.DependencyInjection.Logging;

namespace ActualChat.Testing;

public static class ServiceCollectionExt
{
    public static IServiceCollection ConfigureLogging(this IServiceCollection services, ITestOutputHelper output)
        => ConfigureLogging(services, new TestOutputHelperAccessor(new TimestampedTestOutput(output)));
    public static IServiceCollection ConfigureLogging(this IServiceCollection services, TestOutputHelperAccessor outputAccessor)
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
            // logging.AddFilter("ActualLab.Fusion.EntityFramework", LogLevel.Debug);
            // logging.AddFilter("ActualLab.Fusion.EntityFramework.Operations", LogLevel.Debug);
            // logging.AddFilter(LogFilter);
            logging.AddDebug();
            // XUnit logging requires weird setup b/c otherwise it filters out
            // everything below LogLevel.Information
            logging.AddProvider(
#pragma warning disable CS0618
                new XunitTestOutputLoggerProvider(
                    outputAccessor,
                    (_, _) => true));
#pragma warning restore CS0618
        });
}
