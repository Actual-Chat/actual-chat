namespace ActualChat.Core.UnitTests.Blobs;

public class BlobIdTest
{
    [Fact]
    public void GetScopeTest()
    {
        BlobPath.GetScope("a").Should().Be("");
        BlobPath.GetScope("a/b").Should().Be("a");
    }
}
