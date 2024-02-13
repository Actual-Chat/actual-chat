using ActualLab.Testing.Output;

namespace ActualChat.Testing;

public abstract class TestBase(ITestOutputHelper @out) : IAsyncLifetime
{
    protected ITestOutputHelper Out { get; private set; } = @out;

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
