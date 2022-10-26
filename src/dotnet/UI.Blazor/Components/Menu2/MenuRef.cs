using Cysharp.Text;

namespace ActualChat.UI.Blazor.Components;

public static class MenuRef
{
    private const char Separator = '|';

    public static string Format<TMenu>(params string[] arguments)
        where TMenu : MenuBase
    {
        var buffer = MemoryBuffer<string>.LeaseAndSetCount(false, arguments.Length + 1);
        var span = buffer.Span;
        try {
            span[0] = MenuRegistry.GetTypeId<TMenu>();
            arguments.CopyTo(span.Slice(1));
            return ZString.Join(Separator, (ReadOnlySpan<string>) span);
        }
        finally {
            buffer.Release();
        }
    }

    public static (string MenuType, string[] Arguments) Parse(string trigger)
    {
        var parts = trigger.Split(Separator);
        return (parts[0], parts[1..]);
    }
}
