using Cysharp.Text;

namespace ActualChat.UI.Blazor.Components;

public readonly struct MenuRef
{
    private const char Separator = '|';

    public Type MenuType { get; }
    public string[] Arguments { get; }

    public static MenuRef New<TMenu>()
        where TMenu : IMenu
        => new (typeof(TMenu));
    public static MenuRef New<TMenu>(params string[] arguments)
        where TMenu : IMenu
        => new (typeof(TMenu), arguments);

    public MenuRef(Type menuType)
        : this(menuType, Array.Empty<string>()) { }
    public MenuRef(Type menuType, params string[] arguments)
    {
        MenuType = menuType;
        Arguments = arguments;
    }

    public override string ToString()
    {
        var buffer = MemoryBuffer<string>.LeaseAndSetCount(false, Arguments.Length + 1);
        var span = buffer.Span;
        try {
            span[0] = MenuRegistry.GetTypeId(MenuType).Value;
            Arguments.CopyTo(span.Slice(1));
            return ZString.Join(Separator, (ReadOnlySpan<string>) span);
        }
        finally {
            buffer.Release();
        }
    }

    // Parse & TryParse

    public static MenuRef Parse(string value)
        => TryParse(value, out var result) ? result : throw StandardError.Format<MenuRef>();

    public static bool TryParse(string value, out MenuRef result)
    {
        var parts = value.Split(Separator);
        if (parts.Length == 0) {
            result = default;
            return false;
        }

        var typeId = parts[0];
        result = parts.Length == 1
            ? new MenuRef(MenuRegistry.GetType(typeId))
            : new MenuRef(MenuRegistry.GetType(typeId), parts[1..]);
        return true;
    }
}
