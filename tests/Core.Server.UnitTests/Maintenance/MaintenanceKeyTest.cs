namespace ActualChat.Core.Server.UnitTests.Maintenance;

public sealed class MaintenanceKeyTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData(0x01234567u, 0u)]
    [InlineData(0xabcdef12u, 10u)]
    [InlineData(0xffffffffu, 15u)]
    public void RoutingShouldUseTheHighDigitAndRetainTheKeyPrefix(uint value, uint shard)
    {
        // arrange
        var key = new MaintenanceKey(
            "chat:any/string:key",
            new ShardKey(value).Head(MaintenanceKey.FullPartitionKeySize));

        // act, assert
        key.FullPartitionKey.Size.Should().Be(MaintenanceKey.FullPartitionKeySize);
        key.PartitionKey.Size.Should().Be(MaintenanceKey.PartitionKeySize);
        key.ShardKey.Value.Should().Be(shard);
        key.ToString().Should().Be($"{value >> 16:x4}:chat:any/string:key");
        key.ToString().Length.Should().Be(MaintenanceKey.IdPrefixLength + key.Value.Length);
        key.AssertPassesThroughSerializers(Out);
    }

    [Fact]
    public void EqualRoutingKeysShouldNotConflateDifferentObjects()
    {
        // arrange
        var fullPartitionKey = new ShardKey(0xabcdef12).Head(MaintenanceKey.FullPartitionKeySize);
        var first = new MaintenanceKey("first", fullPartitionKey);
        var second = new MaintenanceKey("second", fullPartitionKey);

        // act, assert
        first.ShardKey.Should().Be(second.ShardKey);
        first.PartitionKey.Should().Be(second.PartitionKey);
        first.ToString().Should().NotBe(second.ToString());
        first.Should().NotBe(second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyKeysShouldBeRejected(string? value)
    {
        // arrange
        var key = new MaintenanceKey(value!, default);

        // act
        var validate = () => key.RequireValid();

        // assert
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(MaintenanceKey.FullPartitionKeySize - 1)]
    [InlineData(MaintenanceKey.FullPartitionKeySize + 1)]
    [InlineData(ShardKey.MaxSize)]
    public void OtherFullPartitionKeySizesShouldBeRejected(int size)
    {
        // arrange
        var key = new MaintenanceKey("chat:any", new ShardKey(0xabcdef12).Head(size));

        // act
        var validate = () => key.RequireValid();

        // assert
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }
}
