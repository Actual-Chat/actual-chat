using ActualLab.IO;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Testing;

public abstract class TestBase(ITestOutputHelper @out, ILogger? log = null) : IAsyncLifetime
{
    public static TimeSpan DefaultTestTimeout { get; set; } = TimeSpan.FromMinutes(2);

    protected ITestOutputHelper Out { get; private set; } = @out.ToSafe();

    protected ILogger Log => field ??= log ?? Out.ToLoggerFactory().CreateLogger(GetType());

    protected virtual void WriteLine(string message)
        => Out.WriteLine(message);

    Task IAsyncLifetime.InitializeAsync() => InitializeAsync();
    protected virtual Task InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync();
    protected virtual Task DisposeAsync() => Task.CompletedTask;

    protected CancellationTokenSource NewTestCts(TimeSpan? timeout = null)
        // The CTS counterpart of TestWait: a hung await fails with TaskCanceledException rather
        // than running to the outer --blame-hang-timeout, and the deadline takes the same
        // build-agent scale the wait budgets take - an unscaled guard fires before what it guards
        => new((timeout ?? DefaultTestTimeout).CiScaled());

    protected static IConfigurationRoot GetConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(GetTestsBaseDirectory())
            .AddJsonFile("testsettings.json", false, false);
        if (EnvExt.IsRunningInContainer())
            builder.AddJsonFile("testsettings.docker.json", false, false);
        builder.AddJsonFile("testsettings.local.json", true, false);
        builder.AddEnvironmentVariables();

        var configuration = builder.Build();
        return configuration;

        static FilePath GetTestsBaseDirectory()
            => FilePath.New(typeof(DefaultStartup).Assembly.Location ?? Environment.CurrentDirectory).DirectoryPath;
    }
}
