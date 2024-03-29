namespace ActualChat.Core.Server.UnitTests.Sharding;

public class ShardSchemeTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        var s = ShardScheme.None;
        s.IsNone.Should().BeTrue();
        s.IsUndefined.Should().BeFalse();
        s.IsValid.Should().BeFalse();
        s.NullIfUndefined().Should().BeSameAs(s);

        s = ShardScheme.Undefined;
        s.IsNone.Should().BeFalse();
        s.IsUndefined.Should().BeTrue();
        s.IsValid.Should().BeFalse();
        s.NullIfUndefined().Should().BeNull();

        s = ShardScheme.TestBackend;
        s.IsNone.Should().BeFalse();
        s.IsUndefined.Should().BeFalse();
        s.IsValid.Should().BeTrue();
        s.NullIfUndefined().Should().BeSameAs(s);
    }
}
