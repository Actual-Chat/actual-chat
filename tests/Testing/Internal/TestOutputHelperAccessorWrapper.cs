using ActualLab.Testing.Output;
using MartinCostello.Logging.XUnit;

namespace ActualChat.Testing.Internal;

public class TestOutputHelperAccessorWrapper(TestOutputHelperAccessor outputAccessor)
    : ITestOutputHelperAccessor
{
    public ITestOutputHelper? OutputHelper {
        get => outputAccessor.Output;
        set => outputAccessor.Output = value;
    }
}
