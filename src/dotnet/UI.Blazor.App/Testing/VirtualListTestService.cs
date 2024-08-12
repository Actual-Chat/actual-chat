namespace ActualChat.UI.Blazor.App.Testing;

public class VirtualListTestService : IComputeService
{
    private static readonly string[] Words = { "best", "virtual", "scroll", "ever", "100%", "absolutely" };

    private Moment Start { get; }
    private MomentClock Clock { get; }

    public VirtualListTestService(IServiceProvider services)
    {
        Clock = services.Clocks().CpuClock;
        Start = Clock.Now;
    }

    [ComputeMethod]
    public virtual async Task<VirtualListData<TestListItemRef>> GetItemKeys(
        VirtualListDataQuery query,
        int? rangeSeed,
        int? contentSeed,
        CancellationToken cancellationToken)
    {
        var rangeSeedValue = rangeSeed ?? await GetSeed(0, 3, cancellationToken).ConfigureAwait(false);
        var range = GetKeyRange(rangeSeedValue);
        var start = range.Start;
        var end = range.End;
        if (!query.IsNone) {
            var queryRange = query.KeyRange;
            start = int.Parse(queryRange.Start, NumberStyles.Integer, CultureInfo.InvariantCulture);
            start += query.MoveRange.Start;
            end = int.Parse(queryRange.End, NumberStyles.Integer, CultureInfo.InvariantCulture);
            end += query.MoveRange.End;
        }

        start = Math.Max(range.Start, start);
        end = Math.Min(range.End, end);

        var result = new VirtualListData<TestListItemRef>(
            Enumerable
                .Range(start, end - start + 1)
                .Select(key => new TestListItemRef(key, rangeSeedValue, contentSeed))
                .Chunk(10)
                .Select(chunk => new VirtualListTile<TestListItemRef>($"tile-{chunk.First().Id}", chunk))
                .ToList()) {
            HasVeryFirstItem = start == range.Start,
            HasVeryLastItem = end == range.End,
        };
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        return result;
    }

    [ComputeMethod]
    public virtual async Task<TestListItem> GetItem(TestListItemRef itemRef, CancellationToken cancellationToken)
    {
        var key = itemRef.Id;
        var keyRange = GetKeyRange(itemRef.RangeSeed);
        var contentSeed = itemRef.ContentSeed ?? await GetSeed(key % 10, 10, cancellationToken).ConfigureAwait(false);
        var rnd = new Random(contentSeed + key);
        var fontSize = 1 + rnd.NextDouble();
        if (fontSize > 1.95)
            fontSize = 3;
        var wordCount = rnd.Next(20);
        if (key == keyRange.End) {
            var wordCountSeed = itemRef.ContentSeed ?? await GetSeed(0, 0.25, cancellationToken).ConfigureAwait(false);
            wordCount = wordCountSeed % 100;
        }
        var description = Enumerable.Range(0, wordCount)
            .Select(_ => Words[rnd.Next(Words.Length)])
            .ToDelimitedString(" ");
        return new TestListItem(key, $"#{key}", description, fontSize);
    }

    [ComputeMethod]
    public virtual Task<int> GetSeed(int offset, double changePeriod, CancellationToken cancellationToken)
    {
        Computed.GetCurrent().Invalidate(TimeSpan.FromSeconds(changePeriod + 0.01));
        return Task.FromResult((int)((offset + (Clock.Now - Start).TotalSeconds) / changePeriod));
    }

    // Protected methods

    protected static Range<int> GetKeyRange(int seed)
    {
        seed = Math.Abs(seed);
        return new Range<int>(-seed / 2, 50 + seed);
    }
}
