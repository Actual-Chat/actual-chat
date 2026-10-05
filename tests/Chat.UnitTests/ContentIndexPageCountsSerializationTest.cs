namespace ActualChat.Chat.UnitTests;

// ContentIndexPageCounts no longer round-trips through Operation.Items, but it stays a
// round-trippable wire type - this pins that, so a rename or a dropped attribute breaks here
// rather than wherever it's next put on the wire.
public class ContentIndexPageCountsSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void PassesThroughAllSerializers()
    {
        var value = NewSample();
        value.AssertPassesThroughSerializers(AssertEqual, Out);
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
