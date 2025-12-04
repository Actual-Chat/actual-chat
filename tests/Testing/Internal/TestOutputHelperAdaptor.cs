using Xunit.DependencyInjection;

namespace ActualChat.Testing.Internal;

public class TestOutputHelperAdaptor(TestOutputHelperAccessor outputAccessor)
    : TestOutputHelperAccessor, MartinCostello.Logging.XUnit.ITestOutputHelperAccessor
{
    public ITestOutputHelper? OutputHelper {
        get => outputAccessor.Output;
        set => outputAccessor.Output = value;
    }
}
