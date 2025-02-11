using Cysharp.Text;

namespace ActualChat.Chat.ML;

public class EntryGroupBuilder
{
    private readonly List<ChatEntry> _entries = [];
    private Utf16ValueStringBuilder _stringBuilder = ZString.CreateStringBuilder();
    private int _wordCount;
    private string? _text;
    private int _averagePauseBetweenEntries;
    private long _minLid = long.MaxValue;
    private long _maxLid = 0;

    public IReadOnlyList<ChatEntry> Entries => _entries;
    public int WordCount => _wordCount;
    public double[] Embeddings { get; set; } = [];
    public int AveragePauseBetweenEntries => _averagePauseBetweenEntries;
    public long MinLid => _minLid;
    public long MaxLid => _maxLid;

    public string Text {
        get {
            if (_text is not null)
                return _text;

            if (_stringBuilder.Length != 0)
                return _text = _stringBuilder.ToString();

            foreach (var entry in _entries)
                _stringBuilder.Append(entry.Content);
            return _text = _stringBuilder.ToString();
        }
    }

    public EntryGroupBuilder()
    { }

    public EntryGroupBuilder(EntryGroup? entryGroup)
    {
        _entries = entryGroup != null ? [..entryGroup.Entries] : [];
        _wordCount = entryGroup?.WordCount ?? 0;
        Initialize();
    }

    public EntryGroupBuilder(IReadOnlyCollection<ChatEntry> entries)
    {
        _entries = [.. entries];
        _wordCount = entries.Sum(entry => CountWords(entry.Content));
        Initialize();
    }

    private void Initialize()
    {
        RecalculateAveragePause();
        foreach (var entry in _entries) {
            if (entry.LocalId < _minLid)
                _minLid = entry.LocalId;
            if (entry.LocalId > _maxLid)
                _maxLid = entry.LocalId;
        }
    }

    public EntryGroupBuilder Add(ChatEntry entry)
    {
        _entries.Add(entry);
        _wordCount += CountWords(entry.Content);
        Embeddings = [];
        _text = null;
        if (_entries.Count > 1) {
            var lastEntry = _entries[^2];
            var currentPause = Math.Max(0, (entry.BeginsAt - (lastEntry.EndsAt ?? lastEntry.BeginsAt)).TotalSeconds);
            _averagePauseBetweenEntries = ((_averagePauseBetweenEntries * (_entries.Count - 2)) + (int)currentPause) / (_entries.Count - 1);
        }
        if (entry.LocalId < _minLid)
            _minLid = entry.LocalId;
        if (entry.LocalId > _maxLid)
            _maxLid = entry.LocalId;

        if (_stringBuilder.Length == 0)
            return this;

        _stringBuilder.Append(entry.Content);
        _stringBuilder.Append('\n');
        return this;
    }

    public EntryGroupBuilder AddRange(IEnumerable<ChatEntry> entries)
    {
        if (entries is ICollection<ChatEntry> entryList) {
            _entries.AddRange(entryList);
            _wordCount += entryList.Sum(entry => CountWords(entry.Content));
            _text = null;
            RecalculateAveragePause();
            if (_stringBuilder.Length != 0)
                _stringBuilder.Clear();
            foreach (var entry in entryList) {
                if (entry.LocalId < _minLid)
                    _minLid = entry.LocalId;
                if (entry.LocalId > _maxLid)
                    _maxLid = entry.LocalId;
            }
        }
        else
            foreach (var entry in entries)
                Add(entry);
        Embeddings = [];
        return this;
    }

    public int GetPauseBetween(ChatEntry entry)
    {
        if (_entries.Count == 0)
            return 0;

        var lastEntry = _entries[^1];
        return (int)Math.Max(0, (entry.BeginsAt - (lastEntry.EndsAt ?? lastEntry.BeginsAt)).TotalSeconds);
    }

    public EntryGroup Build(bool isCompleted = true)
        => new (_entries, _wordCount, isCompleted);


    // Private methods

    private void RecalculateAveragePause()
    {
        if (_entries.Count <= 1)
            return;

        var totalPause = 0;
        for (int i = 1; i < _entries.Count; i++) {
            var previousEntry = _entries[i - 1];
            var currentEntry = _entries[i];
            var currentPause = Math.Max(0, (currentEntry.BeginsAt - (previousEntry.EndsAt ?? previousEntry.BeginsAt)).TotalSeconds);
            totalPause += (int)currentPause;
        }
        _averagePauseBetweenEntries = totalPause / (_entries.Count - 1);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int count = 0;
        bool inWord = false;
        foreach (char c in text)
            if (char.IsWhiteSpace(c))
                inWord = false;
            else {
                if (inWord)
                    continue;

                count++;
                inWord = true;
            }
        return count;
    }
}
