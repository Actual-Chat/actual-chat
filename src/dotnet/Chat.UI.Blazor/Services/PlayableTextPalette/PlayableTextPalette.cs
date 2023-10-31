namespace ActualChat.Chat.UI.Blazor.Services;

public class PlayableTextPalette
{
    private const int HistoryLength = 10;
    private static readonly ApiArray<PlayableTextColor> _colors = ApiArray.New(
        PlayableTextColor.Blue,
        PlayableTextColor.Purple,
        PlayableTextColor.Yellow,
        PlayableTextColor.Green);

    private readonly List<PaletteColorLease> _leasingHistory = new (HistoryLength);
    private readonly Dictionary<PlayableTextColor, PaletteColorLease> _activeLeases = new ();

    public IPlayableTextColorLease RentColor(Symbol authorId, long entryId)
    {
        lock (_activeLeases) {
            var lease = _activeLeases
                .Where(c => c.Value.AuthorId == authorId.Value)
                .Select(c => c.Value)
                .FirstOrDefault();
            if (lease == null) {
                var assignedColor = PeekColor(authorId);
                if (assignedColor.HasValue) {
                    lease = new PaletteColorLease(this, assignedColor.Value, authorId);
                    _activeLeases.Add(assignedColor.Value, lease);
                    _leasingHistory.Insert(0, lease);
                    while (_leasingHistory.Count > HistoryLength)
                        _leasingHistory.RemoveAt(HistoryLength);
                }
            }
            if (lease != null)
                return lease.ActivateLease(entryId);

            // fallback scenario
            return new PlayableTextColorLease(null, PlayableTextColor.Blue, entryId);
        }
    }

    internal void Release(PaletteColorLease lease)
    {
        lock (_activeLeases) {
            if (!_activeLeases.TryGetValue(lease.Color, out var activeLease))
                return;
            if (activeLease != lease)
                return;
            _activeLeases.Remove(lease.Color);
        }
    }

    private PlayableTextColor? PeekColor(Symbol authorId)
    {
        foreach (var oldLease in _leasingHistory.Where(c => c.AuthorId == authorId.Value)) {
            if (!_activeLeases.ContainsKey(oldLease.Color))
                return oldLease.Color;
        }

        int GetColorPriority(PlayableTextColor c) {
            var index = _leasingHistory.FindIndex(l => l.Color == c);
            if (index >= 0)
                return _leasingHistory.Count - index; // recently used color should have lower priority
            return 0;
        }
        var freeColors = _colors.Where(c => !_activeLeases.ContainsKey(c));
        var color = freeColors.OrderBy(GetColorPriority).FirstOrDefault();
        return color;
    }
}
