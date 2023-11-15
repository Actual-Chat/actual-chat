namespace ActualChat.Chat.UI.Blazor.Services;

public class PlayableTextColorLease(PaletteColorLease? owner, PlayableTextColor color, long chatEntryId)
    : IPlayableTextColorLease
{
    private bool _isReleased;

    public PlayableTextColor Color { get; } = color;
    public long ChatEntryId { get; } = chatEntryId;

    public void Release()
    {
        if (_isReleased)
            return;

        _isReleased = true;
        owner?.Release(this);
    }
}
