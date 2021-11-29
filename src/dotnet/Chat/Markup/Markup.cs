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
    private Option<Range<double>?> _cachedTimeRange;

    public Markup Markup { get; init; } = null!;
    public Range<int> TextRange { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Range<double>? TimeRange {
        get {
            if (_cachedTimeRange.HasValue)
                return _cachedTimeRange.Value;
            var ttm = Markup.TextToTimeMap;
            var start = ttm.Map(TextRange.Start);
            if (!start.HasValue)
                return null;
            var startValue = start.GetValueOrDefault();
            _cachedTimeRange = new Range<double>(startValue, ttm.Map(TextRange.End) ?? startValue);
            return _cachedTimeRange.Value;
        }
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ReadOnlySpan<char> TextSpan => Markup.Text.AsSpan(TextRange.Start, TextRange.End - TextRange.Start);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Text => new(TextSpan);

}

public class PlainTextPart : MarkupPart
{ }


