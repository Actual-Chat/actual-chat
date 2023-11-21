using System.Diagnostics.CodeAnalysis;
using Cysharp.Text;

namespace ActualChat.UI.Blazor.Components;

public readonly struct MenuRef(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type menuType,
    params string[] arguments)
{
    private const char Separator = '|';

    public Type MenuType { get; } = menuType;
    public string[] Arguments { get; } = arguments;

    public static MenuRef New<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMenu>()
        where TMenu : IMenu
        => new (typeof(TMenu));
    public static MenuRef New<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMenu>(params string[] arguments)
        where TMenu : IMenu
        => new (typeof(TMenu), arguments);

    public MenuRef(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type menuType)
        : this(menuType, Array.Empty<string>()) { }

    public override string ToString()
    {
        var buffer = MemoryBuffer<string>.LeaseAndSetCount(false, Arguments.Length + 1);
        var span = buffer.Span;
        try {
            span[0] = MenuRegistry.GetTypeId(MenuType).Value;
            for (int i = 0; i < Arguments.Length; i++)
                span[i + 1] = Arguments[i].ToBase64();
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

        for (int i = 1; i < parts.Length; i++)
            parts[i] = parts[i].FromBase64();

        var typeId = parts[0];
        result = parts.Length == 1
 #pragma warning disable IL2072
            ? new MenuRef(MenuRegistry.GetType(typeId))
            : new MenuRef(MenuRegistry.GetType(typeId), parts[1..]);
 #pragma warning restore IL2072
        return true;
    }
}
