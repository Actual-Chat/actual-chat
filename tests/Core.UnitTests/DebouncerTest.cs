using System.Diagnostics;
using ActualLab.Time.Testing;

namespace ActualChat.Core.UnitTests;

public class DebouncerTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task DebounceTest()
    {
        var results = new List<int>();
        using var clock = new TestClock();
        var sw = Stopwatch.StartNew();
        Action<string> log = msg => WriteLine(sw.Elapsed.ToString("c") + " " + msg);
        WriteLine($"start: {sw.Elapsed}");
        var debouncer = Debouncer.New<int>(clock, TimeSpan.FromMilliseconds(2000), i => {
            log($"debounce invoked, value ='{i}'");
            lock (results)
                results.Add(i);
            return Task.CompletedTask;
        });
        debouncer.Debounce(1);
        log.Invoke("after debounce 1");
        clock.OffsetBy(100);
        debouncer.Debounce(2);
        log.Invoke("after debounce 2");
        clock.OffsetBy(100);
        debouncer.Debounce(3);
        log.Invoke("after debounce 3");
        clock.OffsetBy(100);
        await Task.Delay(1); // Just to make sure async ops complete
        log.Invoke("after delay 1");
        debouncer.Debounce(4);
        log.Invoke("after debounce 4");
        clock.OffsetBy(3000);
        log.Invoke("after OffsetBy(3000)");

        await debouncer.WhenCompleted();
        log.Invoke("after WhenCompleted");
        results.Count.Should().Be(1);
        results[0].Should().Be(4);

        results.Clear();
        log.Invoke("after results.Clear()");
        debouncer.Debounce(1, true);
        clock.OffsetBy(100);
        debouncer.Debounce(2, true);
        clock.OffsetBy(100);
        debouncer.Debounce(3, true);
        clock.OffsetBy(100);
        await Task.Delay(300); // Just to make sure async ops complete
        debouncer.Debounce(4, true);
        clock.OffsetBy(2000);

        await debouncer.WhenCompleted();
        results.Count.Should().Be(1);
        results[0].Should().Be(1);

        results.Clear();
        debouncer.Debounce(1, true);
        clock.OffsetBy(100);
        debouncer.Debounce(2, true);
        clock.OffsetBy(100);
        debouncer.Debounce(3, true);
        clock.OffsetBy(2000);
        await Task.Delay(300); // Just to make sure async ops complete
        debouncer.Debounce(4, true);
        clock.OffsetBy(2000);

        await debouncer.WhenCompleted();
        results.Count.Should().Be(2);
        results[0].Should().Be(1);
        results[1].Should().Be(4);
    }

    [Fact]
    public async Task ThrottleTest()
    {
        var results = new List<int>();
        using var clock = new TestClock();
        var debouncer = Debouncer.New<int>(clock, TimeSpan.FromMilliseconds(1000), i => {
            lock (results) {
                results.Add(i);
            }
            return Task.CompletedTask;
        });
        debouncer.Throttle(1);
        clock.OffsetBy(100);
        debouncer.Throttle(2);
        clock.OffsetBy(2000);
        await Task.Delay(300); // Just to make sure async ops complete
        debouncer.Throttle(3);
        clock.OffsetBy(2000);

        await debouncer.WhenCompleted();
        results.Count.Should().Be(2);
        results[0].Should().Be(2);
        results[1].Should().Be(3);

        results.Clear();
        debouncer.Throttle(1, true);
        clock.OffsetBy(100);
        debouncer.Throttle(2, true);
        clock.OffsetBy(2000);
        await Task.Delay(300); // Just to make sure async ops complete
        debouncer.Throttle(3, true);
        clock.OffsetBy(2000);

        await debouncer.WhenCompleted();
        results.Count.Should().Be(2);
        results[0].Should().Be(1);
        results[1].Should().Be(3);
    }
}
