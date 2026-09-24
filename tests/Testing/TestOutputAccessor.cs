using MartinCostello.Logging.XUnit;

namespace ActualChat.Testing;

/// <summary>
/// Unlike Xunit.DependencyInjection's <c>TestOutputHelperAccessor</c>, doesn't keep the output in an
/// <see cref="AsyncLocal{T}"/>: a shared host's background loops run in the execution context
/// of the fixture that started them, so they'd never see the output set by the current test.
/// </summary>
public sealed class TestOutputAccessor : ITestOutputHelperAccessor
{
    public ITestOutputHelper? Output {
        get => Volatile.Read(ref field);
        set => Volatile.Write(ref field, value);
    }

    ITestOutputHelper? ITestOutputHelperAccessor.OutputHelper {
        get => Output;
        set => Output = value;
    }
}
