namespace ActualChat.UI.Blazor.Components;

public static class MenuTrigger
{
    private const string Separator = "::";

    public static string Format<TMenu>(params string[] arguments)
        => string.Join(Separator, new[] { typeof(TMenu).Name }.Concat(arguments));

    public static (string MenuType, ImmutableArray<string> Arguments) Parse(string trigger)
    {
        var parts = trigger.Split(Separator);
        return (parts[0], parts.Skip(1).ToImmutableArray());
    }
}
