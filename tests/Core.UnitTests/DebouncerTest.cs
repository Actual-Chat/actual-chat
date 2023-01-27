using ActualChat.IO;
using ActualChat.Testing.Collections;
using Stl.Time.Testing;

namespace ActualChat.Core.UnitTests;

[Collection(nameof(AppHostTests)), Trait("Category", nameof(AppHostTests))]
public class DebouncerTest
{
    [Fact(Skip = "Flacky")]
    public async Task DebounceTest()
    {
        var results = new List<int>();
        var clock = new TestClock();
        var debouncer = new Debouncer<int>(clock, TimeSpan.FromMilliseconds(1000), i => {
            lock (results) {
                results.Add(i);
            }
            return Task.CompletedTask;
        });
        debouncer.Debounce(1);
        clock.OffsetBy(100);
        debouncer.Debounce(2);
        clock.OffsetBy(100);
        debouncer.Debounce(3);
        clock.OffsetBy(100);
        await Task.Delay(300); // Just to make sure async ops complete
        debouncer.Debounce(4);
        clock.OffsetBy(2000);

        await debouncer.WhenCompleted();
        results.Count.Should().Be(1);
        results[0].Should().Be(4);

        results.Clear();
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

    [Fact(Skip = "Flacky")]
    public async Task ThrottleTest()
    {
        var results = new List<int>();
        var clock = new TestClock();
        var debouncer = new Debouncer<int>(clock, TimeSpan.FromMilliseconds(1000), i => {
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
