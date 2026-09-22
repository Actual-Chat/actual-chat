namespace ActualChat.Chat.UnitTests;

// WebHooksBackend.InvalidatedTokenHashes is set into CommandContext.Operation.Items during the write
// phase of every hook write and read back during the invalidation phase. On nodes other than the one
// that committed the operation that bag arrives via _Operations.ItemsJson — i.e. it goes through
// NewtonsoftJsonSerializer on both ends, so a missing attribute would silently drop the invalidation
// of GetByTokenHash and leave a rotated or deleted token resolving.
public class InvalidatedTokenHashesSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ShouldPassThroughAllSerializers()
    {
        var value = NewSample();
        value.AssertPassesThroughSerializers(AssertEqual, Out);
    }

    [Fact]
    public void ShouldRoundTripViaOperationItems()
    {
        // Mirrors DbOperation.UpdateFrom + ToModel: snapshot the bag, serialize
        // via NewtonsoftJsonSerializer.Default (== DbOperation.Serializer),
        // deserialize back, then KeylessGet.
        var bag = new MutablePropertyBag();
        bag.KeylessSet(NewSample());

        var json = NewtonsoftJsonSerializer.Default.Write(bag.Snapshot);
        Out.WriteLine($"ItemsJson: {json}");

        var roundTripped = NewtonsoftJsonSerializer.Default.Read<PropertyBag>(json).ToMutable();
        var got = roundTripped.KeylessGet<WebHooksBackend.InvalidatedTokenHashes>();

        got.Should().NotBeNull();
        AssertEqual(got!, NewSample());
    }

    private static WebHooksBackend.InvalidatedTokenHashes NewSample()
        => new(["c9yrAd3E1Xx1rGpJ0m7C0w", "u4M2Kx8Lq1nQ9pS7tV3bZa"]);

    private static void AssertEqual(
        WebHooksBackend.InvalidatedTokenHashes actual,
        WebHooksBackend.InvalidatedTokenHashes expected)
        => actual.Hashes.Should().Equal(expected.Hashes);
}
