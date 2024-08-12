namespace ActualChat.UI.Blazor.App.Services;

public interface IPlayableTextColorLease
{
    public PlayableTextColor Color { get; }
    void Release();
}
