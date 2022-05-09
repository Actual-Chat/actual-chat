namespace ActualChat.UI.Blazor.Services;

public class ComponentIdGenerator
{
    private long _lastId = 0;

    public long NextLong() => ++_lastId;

    public string Next(string prefix = "")
    {
        var suffix = NextLong().ToString(CultureInfo.InvariantCulture);
        return prefix.IsNullOrEmpty() ? suffix : $"{prefix}-{suffix}";
    }
}
