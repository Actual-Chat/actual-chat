using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ActualLab.IO;
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
        .ConfigureLogging(log => log
            .SetMinimumLevel(LogLevel.Trace)
            .AddXunitOutput(options => options.Filter = (_, _) => true));

    public virtual void ConfigureServices(IServiceCollection services, HostBuilderContext ctx)
        => services.TryAddSingleton(c => c.LogFor("")); // Default ILogger w/o a category

    private static FilePath GetBaseDirectory()
        => FilePath.New(typeof(DefaultStartup).Assembly.Location ?? Environment.CurrentDirectory).DirectoryPath;
}
