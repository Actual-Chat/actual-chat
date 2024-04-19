using ActualChat.Hashing;

namespace ActualChat.Core.Server.UnitTests.Hashing;

public class HashStringTest
{
    [Theory]
    [InlineData("aaaa")]
    [InlineData("11aaaa")]
    [InlineData("0  1 aaaa")]
    [InlineData("1  1  aaaa")]
    public void ShouldNotParse(string input)
        => new HashString(input, ParseOrNone.Option).Should().Be(HashString.None);

    [Theory]
    [InlineData("3 1 aaaa", HashAlgorithm.SHA256, HashEncoding.Base64, "aaaa")]
    [InlineData("1 0 aaaa", HashAlgorithm.MD5, HashEncoding.Base16, "aaaa")]
    public void ShouldParse(
        string input,
        HashAlgorithm expectedAlgorithm,
        HashEncoding expectedEncoding,
        string expectedHash)
        => new HashString(input).Should()
            .Be(new HashString(expectedAlgorithm, expectedEncoding, new Symbol(expectedHash)));
}
