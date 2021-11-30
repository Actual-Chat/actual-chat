using System.Text.Json.Serialization;
using Stl.Internal;

namespace ActualChat.Chat;

public class Markup
{
    private ImmutableArray<MarkupPart>? _parts;

    public string Text { get; init; } = "";
    public LinearMap TextToTimeMap { get; init; } = default;

    public ImmutableArray<MarkupPart> Parts {
        get => _parts ?? throw Errors.NotInitialized(nameof(Parts));
        set {
            if (_parts != null)
                throw Errors.AlreadyInitialized(nameof(Parts));
            _parts = value;
        }
    }
}

public abstract class MarkupPart
{
    private Range<double> _timeRange;
    private volatile int _isTimeRangeCached;

    public Markup Markup { get; init; } = null!;
    public Range<int> TextRange { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Range<double> TimeRange {
        get {
            if (_isTimeRangeCached != 0)
                return _timeRange;
            var ttm = Markup.TextToTimeMap;
            if (ttm.IsEmpty)
                return default;
            var start = ttm.Map(TextRange.Start);
            if (!start.HasValue)
                return default;
            var startValue = start.GetValueOrDefault();
            _timeRange = new Range<double>(startValue, ttm.Map(TextRange.End) ?? startValue).Normalize();
            Interlocked.Increment(ref _isTimeRangeCached);
            return _timeRange;
        }
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ReadOnlySpan<char> TextSpan => Markup.Text.AsSpan(TextRange.Start, TextRange.End - TextRange.Start);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Text => new(TextSpan);

}

public class PlainTextPart : MarkupPart
{ }


