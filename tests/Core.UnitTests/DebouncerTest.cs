using ActualChat.IO;

namespace ActualChat.Core.UnitTests;

public class DebouncerTest
{
    [Fact]
    public async Task DebounceTest()
    {
        var results = new List<int>();
        var debouncer = new Debouncer<int>(TimeSpan.FromMilliseconds(300), i => {
            lock (results) {
                results.Add(i);
            }
            return Task.CompletedTask;
        });
        debouncer.Debounce(1);
        await Task.Delay(100);
        debouncer.Debounce(2);
        await Task.Delay(100);
        debouncer.Debounce(3);
        await Task.Delay(100);
        debouncer.Debounce(4);

        await debouncer.WhenCompleted();
        results.Count.Should().Be(1);
        results[0].Should().Be(4);

        results.Clear();
        debouncer.Debounce(1, true);
        await Task.Delay(100);
        debouncer.Debounce(2, true);
        await Task.Delay(100);
        debouncer.Debounce(3, true);
        await Task.Delay(100);
        debouncer.Debounce(4, true);

        await debouncer.WhenCompleted();
        results.Count.Should().Be(1);
        results[0].Should().Be(1);
    }

    [Fact]
    public async Task ThrottleTest()
    {
        var results = new List<int>();
        var debouncer = new Debouncer<int>(TimeSpan.FromMilliseconds(200), i => {
            lock (results) {
                results.Add(i);
            }
            return Task.CompletedTask;
        });
        debouncer.Throttle(1);
        await Task.Delay(150);
        debouncer.Throttle(2);
        await Task.Delay(150);
        debouncer.Throttle(3);

        await debouncer.WhenCompleted();
        results.Count.Should().Be(2);
        results[0].Should().Be(2);
        results[1].Should().Be(3);

        results.Clear();
        debouncer.Throttle(1, true);
        await Task.Delay(150);
        debouncer.Throttle(2, true);
        await Task.Delay(150);
        debouncer.Throttle(3, true);

        await debouncer.WhenCompleted();
        results.Count.Should().Be(2);
        results[0].Should().Be(1);
        results[1].Should().Be(3);
    }
}
