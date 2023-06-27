namespace ActualChat.Core.UnitTests.Collections;

public class ApiArrayTest : TestBase
{
    public ApiArrayTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void WithTest()
    {
        var a = Enumerable.Range(0, 5).ToApiArray();
        a.Count.Should().Be(5);

        a.TryAdd(0).Count.Should().Be(5);
        a = a.Add(6);
        a.Count.Should().Be(6);

        a = a.RemoveAll(0);
        a[0].Should().Be(1);
        a.Count.Should().Be(5);

        a = a.RemoveAll(-1);
        a.Count.Should().Be(5);

        a = a.RemoveAll(item => item > 3);
        a.Count.Should().Be(3);
        a = a.RemoveAll((_, index) => index >= 2);
        a.Count.Should().Be(2);

        a = a.Trim(5);
        a.Count.Should().Be(2);

        a = a.Trim(1);
        a.Count.Should().Be(1);
        a[0].Should().Be(1);
    }


    [Fact]
    public void AddOrReplaceTest()
    {
        var a = Enumerable.Range(0, 5).ToApiArray();
        a.Count.Should().Be(5);

        a.AddOrReplace(6).Count.Should().Be(6);

        a.Count.Should().Be(5);
        a = a.Add(6);
        a.Count.Should().Be(6);

        a = a.AddOrReplace(6);
        a.Count.Should().Be(6);

        a = a.AddOrReplace(8);
        a.Count.Should().Be(7);
    }
}
