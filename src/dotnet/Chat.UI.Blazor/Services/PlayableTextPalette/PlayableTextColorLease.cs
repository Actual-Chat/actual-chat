namespace ActualChat.Chat.UI.Blazor.Services;

public class PlayableTextColorLease : IPlayableTextColorLease
{
    private readonly PaletteColorLease? _owner;
    private bool _isReleased;

    public PlayableTextColor Color { get; }
    public long ChatEntryId { get; }

    public PlayableTextColorLease(PaletteColorLease? owner, PlayableTextColor color, long chatEntryId)
    {
        _owner = owner;
        Color = color;
        ChatEntryId = chatEntryId;
    }

    public void Release()
    {
        if (_isReleased)
            return;
        _isReleased = true;
        _owner?.Release(this);
    }
}
