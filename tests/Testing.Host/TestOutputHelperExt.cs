using System.Reflection;

namespace ActualChat.Testing.Host;

public static class TestOutputHelperExt
{
    public static ITest GetTest(this ITestOutputHelper output)
        => (ITest)(output.GetType()
                .GetField("test", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(output)
            ?? throw StandardError.Internal("Failed to extract test name."));
}
