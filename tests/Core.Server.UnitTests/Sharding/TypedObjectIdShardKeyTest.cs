namespace ActualChat.Core.Server.UnitTests.Sharding;

public class TypedObjectIdShardKeyTest
{
    [Theory]
    [InlineData("u:abcdef")]
    [InlineData("c:abcdef")]
    [InlineData("ce:abcdef:0:1")]
    [InlineData("a:abcdef:1")]
    [InlineData("p:abcdefghij")]
    public void TypedIdsShouldUseTheirUnderlyingObjectRouting(string value)
    {
        // arrange
        var id = TypedObjectId.Parse(value);
        var resolver = ShardKeyResolvers.Get<TypedObjectId>();

        // act
        var shardKey = resolver(id);

        // assert
        shardKey.Should().Be(id.ObjectId.ShardKey);
        resolver(null!).Should().Be(default);
    }
}
