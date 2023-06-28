namespace ActualChat.Core.UnitTests.Collections;

public class ApiArrayTest : TestBase
{
    public ApiArrayTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void WithTest()
    {
        var a = Enumerable.Range(0, 5).ToApiArray();
        a.Should().HaveCount(5);

        a.TryAdd(0).Should().HaveCount(5);
        a = a.Add(6);
        a.Should().HaveCount(6);

        a = a.RemoveAll(0);
        a[0].Should().Be(1);
        a.Should().HaveCount(5);

        a = a.RemoveAll(-1);
        a.Should().HaveCount(5);

        a = a.RemoveAll(item => item > 3);
        a.Should().HaveCount(3);
        a = a.RemoveAll((_, index) => index >= 2);
        a.Should().HaveCount(2);

        a = a.Trim(5);
        a.Should().HaveCount(2);

        a = a.Trim(1);
        a.Should().HaveCount(1);
        a[0].Should().Be(1);
    }

    [Fact]
    public void AddOrReplaceTest()
    {
        var a = Enumerable.Range(0, 5).ToApiArray();
        a.Should().HaveCount(5);

        a.AddOrReplace(6).Should().HaveCount(6);

        a.Should().HaveCount(5);
        a = a.Add(6);
        a.Should().HaveCount(6);

        a = a.AddOrReplace(6);
        a.Should().HaveCount(6);

        a = a.AddOrReplace(8);
        a.Should().HaveCount(7);
    }

    [Fact]
    public void AddOrUpdateTest()
    {
        var a = Enumerable.Range(0, 5).ToApiArray();
        a.Should().HaveCount(5);

        a.AddOrUpdate(5, i => i + 100).Should().HaveCount(6);

        a.Should().HaveCount(5);
        a.Should().HaveCount(5);
        a = a.Add(5);
        a.Should().HaveCount(6);

        a = a.AddOrUpdate(5, i => i + 2);
        a.Should().HaveCount(6);
        a[5].Should().Be(7);

        a = a.AddOrUpdate(3, i => i + 2);
        a.Should().HaveCount(6);
        a[3].Should().Be(5);
    }
}
