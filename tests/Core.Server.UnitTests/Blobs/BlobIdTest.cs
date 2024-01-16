namespace ActualChat.Core.Server.UnitTests.Blobs;

public class BlobPathTest
{
    [Fact]
    public void GetScopeTest()
    {
        BlobPath.GetScope("a").Should().Be("");
        BlobPath.GetScope("a/b").Should().Be("a");
    }
}
