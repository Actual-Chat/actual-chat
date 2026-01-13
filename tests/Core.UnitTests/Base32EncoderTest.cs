namespace ActualChat.Core.UnitTests;

public class Base32EncoderTest
{
    [Fact]
    public void EncodeDecodeUintTest()
    {
        Base32Encoder.Encode(0u).Should().Be("0");
        Base32Encoder.Decode("0").Should().Be(0u);

        Base32Encoder.Encode(1u).Should().Be("b");
        Base32Encoder.Decode("b").Should().Be(1u);
        Base32Encoder.Decode("a").Should().Be(0u); // 'a' is 0

        Base32Encoder.Encode(31u).Should().Be("7");
        Base32Encoder.Decode("7").Should().Be(31u);

        Base32Encoder.Encode(32u).Should().Be("ba");
        Base32Encoder.Decode("ba").Should().Be(32u);

        Base32Encoder.Encode(uint.MaxValue).Should().Be("d777777");
        Base32Encoder.Decode("d777777").Should().Be(uint.MaxValue);
    }

    [Fact]
    public void EncodeDecodeLongTest()
    {
        Base32Encoder.Encode(0L).Should().Be("0");
        Base32Encoder.Decode("0").Should().Be(0uL);

        Base32Encoder.Encode(long.MaxValue).Should().Be("h777777777777");
        Base32Encoder.Decode("h777777777777").Should().Be((ulong)long.MaxValue);

        Base32Encoder.Encode(ulong.MaxValue).Should().Be("p777777777777");
        Base32Encoder.Decode("p777777777777").Should().Be(ulong.MaxValue);
    }

    [Fact]
    public void EncodeDecodeBytesTest()
    {
        byte[] data = [0x00, 0xFF, 0x12, 0x34, 0x56];
        string encoded = Base32Encoder.Encode(data);
        byte[] decoded = Base32Encoder.DecodeBytes(encoded);
        decoded.Should().Equal(data);

        Base32Encoder.Encode(Array.Empty<byte>()).Should().Be("");
        Base32Encoder.DecodeBytes("").Should().BeEmpty();

        // Test with 1 byte
        Base32Encoder.DecodeBytes(Base32Encoder.Encode([0x00])).Should().Equal([0x00]);
        Base32Encoder.DecodeBytes(Base32Encoder.Encode([0xFF])).Should().Equal([0xFF]);
    }
}
