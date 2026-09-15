namespace ActualChat.Core.Server.UnitTests.Sharding;

public class TypedObjectIdShardKeyTest
{
    [Theory]
    [InlineData("u:abcdef")]
    [InlineData("c:abcdef")]
    [InlineData("ce:abcdef:0:1")]
    [InlineData("a:abcdef:1")]
    [InlineData("p:abcdefghij")]
    public void TypedIdsShouldRouteByTheirUntypedValue(string value)
    {
        // arrange
        var id = TypedObjectId.Parse(value);
        var resolver = ShardKeyResolvers.Get<TypedObjectId>();

        // act
        var shardKey = resolver(id);

        // assert
        shardKey.Should().Be(ShardKeyResolvers.ForString(id.ObjectId.Value));
        resolver(null!).Should().Be(0);
    }
}
