using Stl.Time.Internal;

namespace ActualChat.Chat.UI.Blazor.Testing;

public class TestListService
{
    private static readonly string[] Words = {"best", "virtual", "scroll", "ever", "100%", "absolutely"};
    private readonly ILogger<TestListService> _log;
    private bool _resetToTop = false;
    private bool _resetToBottom = true;

    public TestListService(ILogger<TestListService> log) => _log = log;

    [ComputeMethod(AutoInvalidateTime = 2)]
    public virtual Task<Range<int>> GetListRange(CancellationToken cancellationToken)
    {
        var count = (int) Math.Round((CoarseClockHelper.Now - CoarseClockHelper.Start).TotalSeconds) / 2;
        return Task.FromResult(new Range<int>(-count / 2, 100 + count));
    }

    [ComputeMethod]
    public virtual async Task<VirtualListData<string>> GetItemKeys(
        VirtualListDataQuery query, CancellationToken cancellationToken)
    {
        var range = await GetListRange(cancellationToken).ConfigureAwait(false);
        if (query.InclusiveRange == default) {
            var key = _resetToBottom ? range.End : _resetToTop ? 0 : range.Start + (range.End - range.Start) / 2;
            query = query with { InclusiveRange = new Range<string>(
                key.ToString(CultureInfo.InvariantCulture),
                (key + 20).ToString(CultureInfo.InvariantCulture)) };
        }

        var start = int.Parse(query.InclusiveRange.Start, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (query.ExpandStartBy > 0)
            start -= (int) query.ExpandStartBy;
        start = Math.Max(range.Start, start);

        var end = int.Parse(query.InclusiveRange.End, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (query.ExpandEndBy > 0)
            end += (int) query.ExpandEndBy;
        end = Math.Min(range.End, end);

        var result = VirtualListData.New(
            Enumerable
                .Range(start, end - start + 1)
                .Select(i => i.ToString(CultureInfo.InvariantCulture)),
            item => item,
            start == range.Start,
            end == range.End);
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        return result;
    }

    [ComputeMethod]
    public virtual async Task<TestListItem> GetItem(string key, CancellationToken cancellationToken)
    {
        var intKey = int.Parse(key, NumberStyles.Integer, CultureInfo.InvariantCulture);
        var seed = await GetSeed(cancellationToken).ConfigureAwait(false);
        var range = await GetListRange(cancellationToken).ConfigureAwait(false);
        var rnd = new Random(intKey + (intKey + seed) / 10);
        var fontSize = 1 + rnd.NextDouble();
        if (fontSize > 1.95)
            fontSize = 3;
        var wordCount = rnd.Next(20);
        if (intKey == range.End)
            wordCount = await GetWordCount(cancellationToken).ConfigureAwait(false);
        return new TestListItem(
            intKey,
            $"#{key}",
            Enumerable.Range(0, wordCount).Select(_ => Words[rnd.Next(Words.Length)]).ToDelimitedString(" "),
            fontSize);
    }

    // Protected methods

    [ComputeMethod(AutoInvalidateTime = 2.5)]
    protected virtual Task<int> GetSeed(CancellationToken cancellationToken)
        => Task.FromResult(
            (int) Math.Round((CoarseClockHelper.Now - CoarseClockHelper.Start).TotalMilliseconds * 67) % 101); // 0..100

    [ComputeMethod(AutoInvalidateTime = 0.25)]
    protected virtual Task<int> GetWordCount(CancellationToken cancellationToken)
        => Task.FromResult(
            (int) Math.Round((CoarseClockHelper.Now - CoarseClockHelper.Start).TotalMilliseconds) / 100 % 100); // 0..99
}
