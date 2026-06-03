namespace ActualChat.Chat.UnitTests;

// ContentIndexPageCounts is set into CommandContext.Operation.Items during the
// write phase of UpdateContentIndex and read back during the invalidation phase.
// On nodes other than the one that committed the operation, that bag arrives
// via _Operations.ItemsJson — i.e. it goes through NewtonsoftJsonSerializer on
// both ends. These tests pin the round-trip so a type rename / missing
// attribute breaks the build instead of silently dropping invalidations.
public class ContentIndexPageCountsSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void PassesThroughAllSerializers()
    {
        var value = NewSample();
        value.AssertPassesThroughAllSerializers(AssertEqual, Out);
    }

    [Fact]
    public void RoundTripsViaOperationItems_Newtonsoft()
    {
        // Mirrors DbOperation.UpdateFrom + ToModel: snapshot the bag, serialize
        // via NewtonsoftJsonSerializer.Default (== DbOperation.Serializer),
        // deserialize back, then KeylessGet.
        var bag = new MutablePropertyBag();
        bag.KeylessSet(NewSample());

        var json = NewtonsoftJsonSerializer.Default.Write(bag.Snapshot);
        Out.WriteLine($"ItemsJson: {json}");

        var roundTripped = NewtonsoftJsonSerializer.Default.Read<PropertyBag>(json).ToMutable();
        var got = roundTripped.KeylessGet<ContentIndexPageCounts>();

        got.Should().NotBeNull();
        AssertEqual(got!, NewSample());
    }

    private static ContentIndexPageCounts NewSample()
        => new(new Dictionary<string, int> {
            ["2026-06"] = 3,
            ["2026-05"] = 7,
            ["2024-01"] = 1,
        });

    private static void AssertEqual(ContentIndexPageCounts actual, ContentIndexPageCounts expected)
        => actual.PageCounts.Should().BeEquivalentTo(expected.PageCounts);
}
