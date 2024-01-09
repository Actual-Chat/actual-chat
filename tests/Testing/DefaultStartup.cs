using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ActualLab.IO;
using Xunit.DependencyInjection;
using Xunit.DependencyInjection.Logging;

namespace ActualChat.Testing;

public class DefaultStartup
{
    public virtual void ConfigureHost(IHostBuilder hostBuilder) => hostBuilder
        .ConfigureHostConfiguration(cfg => {
            var dir = GetBaseDirectory();
            cfg.Sources.Clear();
            cfg.SetBasePath(dir);
            cfg.AddJsonFile("testsettings.json", false, false);
            if (EnvExt.IsRunningInContainer())
                cfg.AddJsonFile("testsettings.docker.json", false, false);
            cfg.AddJsonFile("testsettings.local.json", true, false);
            cfg.AddEnvironmentVariables();
        })
        .ConfigureLogging(log => log.SetMinimumLevel(LogLevel.Trace));

    public void Configure(ILoggerFactory loggerFactory, ITestOutputHelperAccessor accessor)
#pragma warning disable CS0618
#pragma warning disable CA2000 // Call Dispose
        => loggerFactory.AddProvider(new XunitTestOutputLoggerProvider(accessor, (s, level) => true));
#pragma warning restore CA2000
#pragma warning restore CS0618

    public virtual void ConfigureServices(IServiceCollection services, HostBuilderContext ctx)
        => services.TryAddSingleton(c => c.LogFor("")); // Default ILogger w/o a category

    private static FilePath GetBaseDirectory()
        => FilePath.New(typeof(DefaultStartup).Assembly.Location ?? Environment.CurrentDirectory).DirectoryPath;
}
