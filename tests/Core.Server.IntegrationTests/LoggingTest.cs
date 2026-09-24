using ActualChat.Testing.Host;
using ActualChat.Testing.Internal;

namespace ActualChat.Core.Server.IntegrationTests;

public class LoggingTest(ITestOutputHelper @out):  AppHostTestBase($"x-{nameof(LoggingTest)}", TestAppHostOptions.None, @out)
{
    [Fact]
    public async Task LogsToTestOutput()
    {
        var capturingOutput = new CapturingTestOutput(Out);
        await using var host = await NewAppHost();
        host.Output = capturingOutput;
        var log = host.Services.LogFor<LoggingTest>();
        log.LogInformation("Hello, world!");
        capturingOutput.HasMessage("Hello, world!").Should().BeTrue();
    }
}

[Collection(nameof(ServerCollection))]
public class SharedLoggingTest(AppHostFixture fixture, ITestOutputHelper @out):  SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task LogsToTestOutput()
    {
        var capturingOutput = new CapturingTestOutput(Out);
        AppHost.Output = capturingOutput;
        var log = AppHost.Services.LogFor<SharedLoggingTest>();
        log.LogInformation("Hello, world!");
        capturingOutput.HasMessage("Hello, world!").Should().BeTrue();
    }

    [Fact]
    public async Task LogsFromBackgroundWorkToTestOutput()
    {
        // Arrange
        var capturingOutput = new CapturingTestOutput(Out);
        AppHost.Output = capturingOutput;
        var log = AppHost.Services.LogFor<SharedLoggingTest>();

        // Act
        Task logTask;
        // Host loops started with the fixture don't carry the test's execution context either
        using (ExecutionContext.SuppressFlow())
            logTask = Task.Run(() => log.LogInformation("Hello from background work!"));
        await logTask;

        // Assert
        capturingOutput.HasMessage("Hello from background work!").Should().BeTrue();
    }
}
