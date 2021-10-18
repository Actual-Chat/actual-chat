namespace ActualChat.Chat.UI.Blazor.Services;

public class AudioIndexService
{
    private readonly List<List<ChatEntry>> _entries = new ();

    // TODO(AK): add Interval tree & remove unused ranges of cached data
    private readonly List<long> _index = new ();
    private readonly ILogger<AudioIndexService> _log;

    public AudioIndexService(ILogger<AudioIndexService> log)
        => _log = log;

    public virtual void AddAudioEntries(IEnumerable<ChatEntry> audioEntries)
    {
        try {
            foreach (var audioEntry in audioEntries) {
                if (audioEntry.ContentType != ChatContentType.Audio)
                    throw new InvalidOperationException(Invariant(
                        $"Only Audio chat entries supported, but {nameof(audioEntries)} contains Id: {audioEntry.Id}, ContentType: {audioEntry.ContentType}"));

                var entryStart = audioEntry.BeginsAt.EpochOffsetTicks;
                var binarySearchIndex = _index.BinarySearch(entryStart);
                if (binarySearchIndex > 0) {
                    var sameTimeEntries = _entries[binarySearchIndex];
                    if (sameTimeEntries.All(e => e.Id != audioEntry.Id))
                        sameTimeEntries.Add(audioEntry);
                }
                else if (binarySearchIndex == 0) {
                    _index.Insert(0, entryStart);
                    _entries.Insert(0, new List<ChatEntry> { audioEntry });
                }
                else {
                    var newEntryIndex = ~binarySearchIndex;
                    _index.Insert(newEntryIndex, entryStart);
                    _entries.Insert(newEntryIndex, new List<ChatEntry> { audioEntry });
                }
            }
        }
        catch (Exception e) {
            _log.LogError(e, "Error build audio index.");
            throw;
        }
    }

    public virtual ChatEntry FindAudioEntry(ChatEntry entry, TimeSpan offset)
    {
        try {
            var entryStart = entry.BeginsAt.EpochOffsetTicks + offset.Ticks;
            var binarySearchIndex = _index.BinarySearch(entryStart);
            var entryIndex = binarySearchIndex < 0
                ? ~binarySearchIndex
                : binarySearchIndex;
            if (entryIndex >= 1)
                entryIndex--;

            var audioEntry = _entries[entryIndex]
                .Where(ae => ae.EndsAt == null || ae.EndsAt.Value.EpochOffsetTicks >= entryStart)
                .OrderByDescending(ae => ae.AuthorId == entry.AuthorId)
                .First();
            var earliestEntry = audioEntry;
            while (audioEntry != null && --entryIndex >= 0) {
                audioEntry = _entries[entryIndex]
                    .Where(ae => ae.EndsAt == null || ae.EndsAt.Value.EpochOffsetTicks >= entryStart)
                    .OrderByDescending(ae => ae.AuthorId == entry.AuthorId)
                    .FirstOrDefault();
                if (audioEntry != null)
                    earliestEntry = audioEntry;
            }

            return earliestEntry;
        }
        catch (Exception e) {
            _log.LogError(e, "Error finding Audio chat entry.");
            throw;
        }
    }
}
