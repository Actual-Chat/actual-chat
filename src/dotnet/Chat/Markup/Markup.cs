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
