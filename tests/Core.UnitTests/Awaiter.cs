using System.Diagnostics;

namespace ActualChat.Core.UnitTests;

public static class Awaiter
{
    public static async Task WaitFor(Func<bool> func)
    {
        if (func == null)
            throw new ArgumentNullException(nameof(func));

        var delay = Debugger.IsAttached ? TimeSpan.FromHours(1) : TimeSpan.FromSeconds(10);
        using var cts = new CancellationTokenSource(delay);

        while (true)
        {
            var result = func();
            if (result)
                return;

            await Task.Delay(100, cts.Token);
        }
    }
}
