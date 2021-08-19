using ActualChat.Audio.Ebml;
using FluentAssertions;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests
{
    public class VIntTests : TestBase
    {
        public VIntTests(ITestOutputHelper @out) : base(@out)
        {
        }

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
    }
}