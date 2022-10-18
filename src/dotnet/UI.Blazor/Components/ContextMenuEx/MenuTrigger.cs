namespace ActualChat.UI.Blazor.Components;

public static class MenuTrigger
{
    public static string Format<TMenu>(params string[] arguments)
        => string.Join(":", new[] { typeof(TMenu).Name }.Concat(arguments));

    public static (string, ImmutableArray<string>) Parse(string trigger)
    {
        var parts = trigger.Split(":");
        return (parts[0], parts.Skip(1).ToImmutableArray());
    }
}
