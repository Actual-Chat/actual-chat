using Cysharp.Text;

namespace ActualChat.UI.Blazor.Components;

public readonly struct BubbleRef
{
    private const char Separator = '|';

    public Type BubbleType { get; }
    public string Group { get; }
    public int Order { get; }

    public static BubbleRef New<TBubble>(string group, int order)
        where TBubble : IBubble
        => new (typeof(TBubble), group, order);

    public BubbleRef(Type bubbleType, string group, int order)
    {
        BubbleType = bubbleType;
        Group = group;
        Order = order;
    }

    public override string ToString()
    {
        var buffer = MemoryBuffer<string>.LeaseAndSetCount(false, 3);
        var span = buffer.Span;
        try {
            span[0] = BubbleRegistry.GetTypeId(BubbleType).Value;
            span[1] = Group;
            span[2] = Order.ToString(CultureInfo.InvariantCulture);
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
        if (parts.Length != 3) {
            result = default;
            return false;
        }

        var typeId = parts[0];
        var group = parts[1];
        var order = int.Parse(parts[2], CultureInfo.InvariantCulture);
        result = new BubbleRef(BubbleRegistry.GetType(typeId), group, order);
        return true;
    }
}
