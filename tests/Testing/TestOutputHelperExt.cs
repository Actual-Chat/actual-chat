using ActualChat.Performance;

namespace ActualChat.Testing;

public static class TestOutputHelperExt
{
    public static ITest GetTest(this ITestOutputHelper output)
        => (ITest)(output.GetType()
                .GetField("test", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(output)
            ?? throw StandardError.Internal("Failed to extract test name."));

    public static Tracer NewTracer(this ITestOutputHelper output, [CallerMemberName] string name = "")
        => new (name, x => output.WriteLine(x.Format()));
}
