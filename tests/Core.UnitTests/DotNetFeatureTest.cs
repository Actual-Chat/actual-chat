namespace ActualChat.Core.UnitTests;

public class DotNetFeatureTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact(Skip = "Just to test how type casts w/ nullable modifier works")]
    public void CastTest()
    {
        var obj = (object?)new Hashtable();
        var nil = (object?)null;

        // All tests below pass
        ((Hashtable?)obj).Should().NotBeNull();
        ((object?)obj).Should().NotBeNull();
        ((Hashtable?)nil).Should().BeNull();
        ((object?)nil).Should().BeNull();
        ((Hashtable)obj).Should().NotBeNull();
        ((object)obj).Should().NotBeNull();
        ((Hashtable)nil).Should().BeNull();
        ((object)nil).Should().BeNull();
    }
}
