namespace ActualChat.Core.UnitTests.Mathematics;

public class UInt128ExtTest : TestBase
{
    public UInt128ExtTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void BasicTest()
    {
        var v = default(UInt128);
        v.ResetBit(0);
        v.Should().Be(default);
        v.IsBitSet(0).Should().BeFalse();

        v.SetBit(0);
        v.Should().Be(1);
        v.IsBitSet(0).Should().BeTrue();
        v.IsBitSet(1).Should().BeFalse();

        v.SetBit(1);
        v.Should().Be(3);
        v.IsBitSet(0).Should().BeTrue();
        v.IsBitSet(1).Should().BeTrue();
    }
}
