namespace ActualChat.UI.Blazor.App.Services;

public class PaletteColorLease
{
    private readonly PlayableTextPalette? _palette;
    private readonly List<PlayableTextColorLease> _activeLeases;
    private bool _isReleased;

    public PlayableTextColor Color { get; }
    public Symbol AuthorId { get; }

    public PaletteColorLease(PlayableTextPalette? palette, PlayableTextColor color, Symbol authorId)
    {
        _palette = palette;
        _activeLeases = new List<PlayableTextColorLease>();
        Color = color;
        AuthorId = authorId;
    }

    public PlayableTextColorLease ActivateLease(long chatEntryId)
    {
        lock (_activeLeases) {
            var activeLease = _activeLeases.FirstOrDefault(c => c.ChatEntryId == chatEntryId);
            if (activeLease == null) {
                activeLease = new PlayableTextColorLease(this, Color, chatEntryId);
                _activeLeases.Add(activeLease);
            }
            return activeLease;
        }
    }

    public void Release(PlayableTextColorLease lease)
    {
        lock (_activeLeases) {
            if (_isReleased)
                return;
            var activeLease = _activeLeases.FirstOrDefault(c => c.ChatEntryId == lease.ChatEntryId);
            if (activeLease != null)
                _activeLeases.Remove(activeLease);
            if (_activeLeases.Count > 0)
                return;
            _isReleased = true;
            _palette?.Release(this);
        }
    }
}
