namespace ActualChat.Core.UnitTests.Identifiers;

public abstract class ContentIdTestBase<TIdentifier>(ITestOutputHelper @out)
    : StringIdentifierTestBase<TIdentifier>(@out)
    where TIdentifier : ContentId, IStringIdentifier<TIdentifier>
{
    [Fact]
    public void ContentRefsShouldRoundTripAndKeepTheirShardKey()
    {
        // arrange
        var identifiers = ValidIdentifiers.Select(TIdentifier.Parse).ToArray();

        // act, assert
        foreach (var id in identifiers) {
            var contentRef = id.ContentRef;
            contentRef.ContentId.Should().BeSameAs(id);
            ContentRef.Parse(contentRef.Value).ContentId.Should().Be(id);
            contentRef.ShardKey.Should().Be(id.ShardKey);
            contentRef.AssertPassesThroughSerializers(Out);
        }
    }
}
