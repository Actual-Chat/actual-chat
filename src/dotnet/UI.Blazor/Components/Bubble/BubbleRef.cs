namespace ActualChat.UI.Blazor.Components;

public readonly struct BubbleRef
{
    public Type BubbleType { get; }

    public static BubbleRef New<TBubble>()
        where TBubble : IBubble
        => new (typeof(TBubble));

    public BubbleRef(Type bubbleType)
        => BubbleType = bubbleType;

    public override string ToString()
        => BubbleRegistry.GetTypeId(BubbleType).Value;

    // Parse & TryParse

    public static BubbleRef Parse(string value)
        => TryParse(value, out var result) ? result : throw StandardError.Format<BubbleRef>();

    public static bool TryParse(string value, out BubbleRef result)
    {
        if (value.IsNullOrEmpty()) {
            result = default;
            return false;
        }

        result = new BubbleRef(BubbleRegistry.GetType(value));
        return true;
    }
}
