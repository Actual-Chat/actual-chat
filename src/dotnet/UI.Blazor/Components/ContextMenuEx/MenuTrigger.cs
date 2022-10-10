namespace ActualChat.UI.Blazor.Components;

public static class MenuTrigger
{
    public static string Format<TMenu>(string? argument = null)
        => $"{typeof(TMenu).Name}:{argument}";

    public static (string menu, string? argument) Parse(string trigger)
    {
        var parts = trigger.Split(":");
        return (parts[0], parts.ElementAtOrDefault(1));
    }
}
