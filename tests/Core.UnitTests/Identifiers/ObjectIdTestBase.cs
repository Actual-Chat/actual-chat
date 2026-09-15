namespace ActualChat.Core.UnitTests.Identifiers;

public abstract class ObjectIdTestBase<TIdentifier>(ITestOutputHelper @out)
    : StringIdentifierTestBase<TIdentifier>(@out)
    where TIdentifier : ObjectId, IStringIdentifier<TIdentifier>
{
    [Fact]
    public void TypedIdsShouldRoundTripAndKeepTheirPartition()
    {
        // arrange
        var identifiers = ValidIdentifiers.Select(TIdentifier.Parse).ToArray();

        // act, assert
        foreach (var id in identifiers) {
            var typedId = id.TypedId;
            typedId.ObjectId.Should().BeSameAs(id);
            TypedObjectId.Parse(typedId.Value).ObjectId.Should().Be(id);
            typedId.ShardKey.Should().Be(id.ShardKey);
            typedId.AssertPassesThroughSerializers(Out);
        }
    }
}
