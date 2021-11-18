using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stl.IO;
using Xunit.DependencyInjection;
using Xunit.DependencyInjection.Logging;

namespace ActualChat.Testing
{
    public class StartupBase
    {
        public virtual void ConfigureHost(IHostBuilder hostBuilder) => hostBuilder
                .ConfigureHostConfiguration(cfg => {
                    var dir = GetBaseDirectory();
                    cfg.Sources.Clear();
                    cfg.SetBasePath(dir);
                    cfg.AddJsonFile("testsettings.json", optional: false, reloadOnChange: false);
                    if (IsRunningInContainer()) {
                        cfg.AddJsonFile("testsettings.docker.json", optional: false, reloadOnChange: false);
                    }
                    cfg.AddJsonFile("testsettings.local.json", optional: true, reloadOnChange: false);
                });

        public void Configure(ILoggerFactory loggerFactory, ITestOutputHelperAccessor accessor) =>
            loggerFactory.AddProvider(new XunitTestOutputLoggerProvider(accessor));

        public virtual void ConfigureServices(IServiceCollection services, HostBuilderContext ctx)
        {
            var settings = new TestSettings();

#pragma warning disable IL2026
            ctx.Configuration.Bind(settings);
#pragma warning restore IL2026
            InitializeSettingsCore(settings);
            InitializeSettings(settings);

            services.TryAddSingleton(settings);
            services.TryAddSingleton(c => c.LogFor("")); // Default ILogger w/o a category
        }

        private void InitializeSettingsCore(TestSettings settings)
        {
            if (string.IsNullOrEmpty(settings.TempDirectory))
                settings.TempDirectory = GetBaseDirectory() & "tmp";

            settings.IsRunningInContainer = IsRunningInContainer();
        }

        private static FilePath GetBaseDirectory()
            => FilePath.New(typeof(StartupBase).Assembly.Location ?? Environment.CurrentDirectory).DirectoryPath;

        protected virtual bool IsRunningInContainer() =>
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != null;

        protected virtual void InitializeSettings(TestSettings settings) { }
    }
}


