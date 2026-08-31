namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Thread-safe accumulator of half-open <c>[Start, End)</c> lid ranges, merged on insert.
/// </summary>
internal sealed class LidRangeSet
{
    private readonly Lock _lock = new();
    private readonly List<Range<long>> _ranges = new();

    public void Add(Range<long> range)
    {
        if (range.IsEmptyOrNegative)
            return;

        lock (_lock) {
            var index = -1;
            for (var i = 0; i < _ranges.Count; i++) {
                if (_ranges[i].End >= range.Start) {
                    index = i;
                    break;
                }
            }

            if (index < 0) {
                _ranges.Add(range);
                return;
            }

            if (_ranges[index].Start > range.End) {
                _ranges.Insert(index, range);
                return;
            }

            // Overlaps or touches _ranges[index]; absorb every subsequent range it reaches
            var merged = _ranges[index].MinMaxWith(range);
            var endIndex = index + 1;
            while (endIndex < _ranges.Count && _ranges[endIndex].Start <= merged.End) {
                merged = merged.MinMaxWith(_ranges[endIndex]);
                endIndex++;
            }
            _ranges[index] = merged;
            _ranges.RemoveRange(index + 1, endIndex - index - 1);
        }
    }

    public bool Intersects(Range<long> range)
    {
        if (range.IsEmptyOrNegative)
            return false;

        lock (_lock) {
            foreach (var r in _ranges) {
                if (r.Start >= range.End)
                    return false;
                if (r.Overlaps(range))
                    return true;
            }

            return false;
        }
    }
}
