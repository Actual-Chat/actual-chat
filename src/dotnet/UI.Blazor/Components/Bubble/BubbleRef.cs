using Cysharp.Text;

namespace ActualChat.UI.Blazor.Components;

public readonly struct BubbleRef
{
    private const char Separator = '|';

    public Type BubbleType { get; }
    public string[] Arguments { get; }

    public static BubbleRef New<TBubble>()
        where TBubble : IBubble
        => new (typeof(TBubble));
    public static BubbleRef New<TBubble>(params string[] arguments)
        where TBubble : IBubble
        => new (typeof(TBubble), arguments);

    public BubbleRef(Type bubbleType)
        : this(bubbleType, Array.Empty<string>()) { }
    public BubbleRef(Type bubbleType, params string[] arguments)
    {
        BubbleType = bubbleType;
        Arguments = arguments;
    }

    public override string ToString()
    {
        var buffer = MemoryBuffer<string>.LeaseAndSetCount(false, Arguments.Length + 1);
        var span = buffer.Span;
        try {
            span[0] = BubbleRegistry.GetTypeId(BubbleType).Value;
            Arguments.CopyTo(span.Slice(1));
            return ZString.Join(Separator, (ReadOnlySpan<string>) span);
        }
        finally {
            buffer.Release();
        }
    }

    // Parse & TryParse

    public static BubbleRef Parse(string value)
        => TryParse(value, out var result) ? result : throw StandardError.Format<BubbleRef>();

    public static bool TryParse(string value, out BubbleRef result)
    {
        var parts = value.Split(Separator);
        if (parts.Length == 0) {
            result = default;
            return false;
        }

        var typeId = parts[0];
        result = parts.Length == 1
            ? new BubbleRef(BubbleRegistry.GetType(typeId))
            : new BubbleRef(BubbleRegistry.GetType(typeId), parts[1..]);
        return true;
    }
}
