using ActualLab.Testing.Output;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Testing;

public abstract class TestBase(ITestOutputHelper @out, ILogger? log = null) : IAsyncLifetime
{
    protected ITestOutputHelper Out { get; private set; } = @out.ToSafe();
    protected ILogger Log { get; } = log ?? NullLogger.Instance;

    Task IAsyncLifetime.InitializeAsync() => InitializeAsync();
    protected virtual Task InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync();
    protected virtual Task DisposeAsync() => Task.CompletedTask;

    protected Disposable<TestOutputCapture> CaptureOutput()
    {
        var testOutputCapture = new TestOutputCapture(Out);
        var oldOut = Out;
        Out = testOutputCapture;
        return new Disposable<TestOutputCapture>(
            testOutputCapture,
            _ => Out = oldOut);
    }
}
