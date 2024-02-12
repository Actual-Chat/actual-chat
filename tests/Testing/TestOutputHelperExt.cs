using ActualChat.Performance;

namespace ActualChat.Testing;

public static class TestOutputHelperExt
{
    public static ITestOutputHelper WithTimestamps(this ITestOutputHelper output)
    {
        if (output is TimestampedTestOutput)
            return output;

        return output is NullTestOutput ? output : new TimestampedTestOutput(output);
    }

    public static ITestOutputHelper GetWrappedOutput(this ITestOutputHelper output)
    {
        while (output is ITestOutputWrapper w)
            output = w.Wrapped;
        return output;
    }

    public static string GetInstanceName(this ITestOutputHelper output)
    {
        var test = output.GetTest()
            ?? throw StandardError.Internal("Failed to extract test info.");
        return test.TestCase.Traits.GetValueOrDefault("Category")?.FirstOrDefault()
            ?? throw StandardError.Internal("Failed to extract test category.");
    }

    public static ITest? GetTest(this ITestOutputHelper output)
    {
        output = output.GetWrappedOutput();
        return output.GetType()
            .GetField("test", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(output) as ITest;
    }

    public static Tracer NewTracer(this ITestOutputHelper output, [CallerMemberName] string name = "")
        => new (name, x => output.WriteLine(x.Format()));
}
