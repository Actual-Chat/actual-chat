using Stl.Testing.Output;
using Xunit.DependencyInjection.Logging;

namespace ActualChat.Testing;

public static class ServiceCollectionExt
{
    public static IServiceCollection ConfigureLogging(this IServiceCollection services, ITestOutputHelper output)
        => services.AddLogging(logging => {
            // Overriding default logging to more test-friendly one
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Debug);
            // logging.AddFilter(DbLoggerCategory.Update.Name, LogLevel.Information);
            // logging.AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Information);
            // logging.AddFilter(DbLoggerCategory.Database.Transaction.Name, LogLevel.Debug);
            logging.AddFilter("Stl.CommandR", LogLevel.Information);
            logging.AddFilter("Stl.Fusion", LogLevel.Information);
            logging.AddFilter("Stl.Fusion.Diagnostics", LogLevel.Information);
            logging.AddFilter("Stl.Fusion.Operations", LogLevel.Information);
            // logging.AddFilter("Stl.Fusion.EntityFramework", LogLevel.Debug);
            // logging.AddFilter("Stl.Fusion.EntityFramework.Operations", LogLevel.Debug);
            // logging.AddFilter(LogFilter);
            logging.AddDebug();
            // XUnit logging requires weird setup b/c otherwise it filters out
            // everything below LogLevel.Information
            logging.AddProvider(
 #pragma warning disable CS0618
                new XunitTestOutputLoggerProvider(
                    new TestOutputHelperAccessor(
                        new TimestampedTestOutput(output)),
                    (_, _) => true));
 #pragma warning restore CS0618
        });
}
