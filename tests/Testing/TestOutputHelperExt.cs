using ActualChat.Performance;
using ActualChat.Testing.Internal;

namespace ActualChat.Testing;

public static class TestOutputHelperExt
{
    public static Tracer NewTracer(this ITestOutputHelper output, [CallerMemberName] string name = "")
        => new (name, x => {
            output.WriteLine(x.Format());
        });

    public static ITestOutputHelper ToSafe(this ITestOutputHelper output)
    {
        if (output is SafeTestOutput or MessageSinkTestOutput)
            return output;

        return output is NullTestOutput ? output : new SafeTestOutput(output);
    }

    public static ITestOutputHelper ToTimestamped(this ITestOutputHelper output)
    {
        var current = output;
        while (current is ITestOutputWrapper wrapper) {
            if (current is TimestampedTestOutput)
                return output;
            current = wrapper.Wrapped;
        }

        return output is NullTestOutput ? output : new TimestampedTestOutput(output);
    }

    public static ITestOutputHelper Unwrap(this ITestOutputHelper output)
    {
        while (output is ITestOutputWrapper wrapper)
            output = wrapper.Wrapped;
        return output;
    }

    public static ITest? GetTest(this ITestOutputHelper output)
    {
        output = output.Unwrap();
        return output.GetType()
            .GetField("test", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(output) as ITest;
    }
}
