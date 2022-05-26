namespace ActualChat.UI.Blazor.Services;

public enum Theme { Light, Dark }

public class ThemeUI
{
    public IMutableState<Theme> CurrentTheme { get; }

    public ThemeUI(IStateFactory stateFactory)
        => CurrentTheme = stateFactory.NewMutable<Theme>();
}
