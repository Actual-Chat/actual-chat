namespace ActualChat.Chat.UI.Blazor.Services;

public interface IPlayableTextColorLease
{
    public PlayableTextColor Color { get; }
    void Release();
}
