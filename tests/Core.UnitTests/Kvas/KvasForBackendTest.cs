using ActualChat.Kvas;

namespace ActualChat.Core.UnitTests.Kvas;

public class KvasForBackendTest : TestBase
{
    public KvasForBackendTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task BasicTest()
    {
        var kvasBackend = new TestKvasBackend() { Out = Out };
        var kvas = new KvasForBackend(new(), kvasBackend);

        kvas.Set("a", "a");
        (await kvas.Get("a")).Should().Be("a");
        kvas.Set("b", "b");
        (await kvas.Get("b")).Should().Be("b");
        kvas.Set("c", "c");
        (await kvas.Get("c")).Should().Be("c");
        kvas.Remove("b");
        (await kvas.Get("b")).Should().Be(null);
        await kvas.Flush();

        var kvas2 = new KvasForBackend(new(), kvasBackend);
        var aTask = kvas2.Get("a");
        var bTask = kvas2.Get("b");
        var cTask = kvas2.Get("c");
        (await aTask).Should().Be("a");
        (await bTask).Should().Be(null);
        (await cTask).Should().Be("c");
    }
}
