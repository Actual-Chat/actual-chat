using System.Text;

namespace ActualChat.Chat.ML;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial class EntryGroupBuilder
{
    private readonly List<ChatEntrySlim> _entries = [];
    private readonly StringBuilder _stringBuilder = new();
    private int _wordCount;
    private string? _text;
    private int _averagePauseBetweenEntries;
    private long _minLid = long.MaxValue;
    private long _maxLid = 0;

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public IReadOnlyList<ChatEntrySlim> Entries => _entries;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public IDictionary<AuthorId, int> AuthorActivity { get; } = new Dictionary<AuthorId, int>();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int WordCount => _wordCount;

    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public double[] Embeddings { get; set; } = [];

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int AveragePauseBetweenEntries => _averagePauseBetweenEntries;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public long MinLid => _minLid;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public long MaxLid => _maxLid;

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string Text {
        get {
            if (_text is not null)
                return _text;

            if (_stringBuilder.Length != 0)
                return _text = _stringBuilder.ToString();

            foreach (var entry in _entries) {
                _stringBuilder.Append(entry.Content);
                _stringBuilder.Append('\n');
            }
            return _text = _stringBuilder.ToString();
        }
    }

    public EntryGroupBuilder()
    { }

    public EntryGroupBuilder(EntryGroupBuilder? entryGroupBuilder)
    {
        _entries = entryGroupBuilder != null ? [..entryGroupBuilder.Entries] : [];
        Initialize();
    }

    public EntryGroupBuilder(EntryGroup? entryGroup)
    {
        _entries = entryGroup != null ? [..entryGroup.Entries] : [];
        Initialize();
    }

    [JsonConstructor, MemoryPackConstructor, SerializationConstructor]
    public EntryGroupBuilder(IReadOnlyList<ChatEntrySlim> entries)
    {
        _entries = [.. entries];
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
            var entryWordCount = CountWords(entry.Content);
            AuthorActivity[entry.AuthorId] = AuthorActivity.TryGetValue(entry.AuthorId, out var count)
                ? count + entryWordCount
                : entryWordCount;
            _wordCount += entryWordCount;
        }
    }

    public EntryGroupBuilder Add(ChatEntrySlim entry)
    {
        var currentPause = GetPauseBetween(entry);
        _entries.Add(entry);
        var entryWordCount = CountWords(entry.Content);
        _wordCount += entryWordCount;
        AuthorActivity[entry.AuthorId] = AuthorActivity.TryGetValue(entry.AuthorId, out var count)
            ? count + entryWordCount
            : entryWordCount;
        _text = null;
        if (_entries.Count > 1)
            _averagePauseBetweenEntries = ((_averagePauseBetweenEntries * (_entries.Count - 2)) + currentPause) / (_entries.Count - 1);
        if (entry.LocalId < _minLid)
            _minLid = entry.LocalId;
        if (entry.LocalId > _maxLid)
            _maxLid = entry.LocalId;

        Embeddings = [];
        if (_stringBuilder.Length == 0)
            return this;

        if (!entry.Content.IsNullOrEmpty()) {
            _stringBuilder.Append(entry.Content);
            _stringBuilder.Append('\n');
        }
        return this;
    }

    public EntryGroupBuilder AddRange(IEnumerable<ChatEntrySlim> entries)
    {
        if (entries is ICollection<ChatEntrySlim> entryList) {
            _entries.AddRange(entryList);
            _text = null;
            RecalculateAveragePause();
            if (_stringBuilder.Length != 0)
                _stringBuilder.Clear();
            foreach (var entry in entryList) {
                var entryWordCount = CountWords(entry.Content);
                _wordCount += entryWordCount;
                AuthorActivity[entry.AuthorId] = AuthorActivity.TryGetValue(entry.AuthorId, out var count)
                    ? count + entryWordCount
                    : entryWordCount;
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

    public int GetPauseBetween(ChatEntrySlim entry)
    {
        if (_entries.Count == 0)
            return 0;

        var lastEntryTime = _entries.Max(e => e.EndsAt ?? e.BeginsAt);
        return (int)Math.Max(0, (entry.BeginsAt - lastEntryTime).TotalSeconds);
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
