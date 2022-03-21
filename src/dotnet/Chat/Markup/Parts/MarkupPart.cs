using System.Text.Json.Serialization;

namespace ActualChat.Chat;

public abstract class MarkupPart
{
    private object? _cachedTimeRange;

    public Markup Markup { get; init; } = null!;
    public Range<int> TextRange { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Range<double> TimeRange {
        get {
            if (_cachedTimeRange != null) return (Range<double>) _cachedTimeRange;
#pragma warning disable RCS1059, MA0064
            lock (this) { // Double-check locking
#pragma warning restore RCS1059, MA0064
                if (_cachedTimeRange != null) return (Range<double>) _cachedTimeRange;
                var ttm = Markup.TextToTimeMap;
                if (ttm.IsEmpty)
                    return default;
                var start = ttm.TryMap(TextRange.Start) ?? 1000_000;
                var end = ttm.TryMap(TextRange.End) ?? start;
                var result = new Range<double>(start, end);
                // ReSharper disable once HeapView.BoxingAllocation
                _cachedTimeRange = result;
                return result;
            }
        }
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ReadOnlySpan<char> TextSpan => Markup.Text.AsSpan(TextRange.Start, TextRange.End - TextRange.Start);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Text => new(TextSpan);
}
