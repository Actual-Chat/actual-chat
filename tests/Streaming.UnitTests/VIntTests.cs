namespace ActualChat.Streaming.UnitTests;

public class VIntTests
{
    [Fact]
    public void EncodeSizeSingleByteTest()
    {
        var vint = VInt.EncodeSize(31);
        vint.Length.Should().Be(1);
        vint.EncodedValue.Should().Be(0x9FUL);
    }

    [Fact]
    public void EncodeSizeTwoBytesTest()
    {
        var vint = VInt.EncodeSize(12910);
        vint.Length.Should().Be(2);
        vint.EncodedValue.Should().Be(0x726E);
    }

    [Fact]
    public void EncodeSizeThreeBytesTest()
    {
        var vint = VInt.EncodeSize(45678);
        vint.Length.Should().Be(3);
        vint.EncodedValue.Should().Be(0x20B26E);
    }

    [Theory]
    [InlineData(0, 1, 0x80ul)]
    [InlineData(1, 1, 0x81ul)]
    [InlineData(126, 1, 0xfeul)]
    [InlineData(127, 2, 0x407ful)]
    [InlineData(128, 2, 0x4080ul)]
    [InlineData(0xdeffad, 4, 0x10deffadul)]
    public void EncodeSize(int value, uint expectedLength, ulong expected)
    {
        var v = VInt.EncodeSize((ulong)value);
        Assert.Equal(expectedLength, v.Length);
        Assert.Equal(expected, v.EncodedValue);
    }

}
