namespace ActualChat.Core.Server.UnitTests.Sharding;

public class ContentRefShardKeyTest
{
    [Theory]
    [InlineData("u:abcdef")]
    [InlineData("c:abcdef")]
    [InlineData("e:abcdef:0:1")]
    [InlineData("a:abcdef:1")]
    [InlineData("p:abcdefghij")]
    public void ContentRefsShouldUseTheirUnderlyingObjectRouting(string value)
    {
        // arrange
        var id = ContentRef.Parse(value);
        var resolver = ShardKeyResolvers.Get<ContentRef>();

        // act
        var shardKey = resolver(id);

        // assert
        shardKey.Should().Be(id.ContentId.ShardKey);
        resolver(null!).Should().Be(default);
    }
}
