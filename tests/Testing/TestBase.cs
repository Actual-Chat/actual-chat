using ActualLab.Testing.Output;

namespace ActualChat.Testing;

public abstract class TestBase : IAsyncLifetime
{
    public ITestOutputHelper Out { get; set; }

    protected TestBase(ITestOutputHelper @out) => Out = @out;

    public virtual Task InitializeAsync() => Task.CompletedTask;
    public virtual Task DisposeAsync() => Task.CompletedTask;

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
