namespace ActualChat.Collections;

public class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _dictionary;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _list;

    public int Capacity { get; }
    public int Count => _list.Count;

    public TValue this[TKey key] {
        get {
            var node = _dictionary[key];
            PopUp(node);
            return node.Value.Value;
        }
        set {
            var pair = KeyValuePair.Create(key, value);
            if (_dictionary.TryGetValue(key, out var node)) {
                node.Value = pair;
                PopUp(node);
            }
            else {
                node = _list.AddFirst(pair);
                _dictionary.Add(key, node);
                Trim();
            }
        }
    }

    public LruCache(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        _dictionary = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity);
        _list = new LinkedList<KeyValuePair<TKey, TValue>>();
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (!_dictionary.TryGetValue(key, out var node)) {
            value = default!;
            return false;
        }

        value = node.Value.Value;
        PopUp(node);
        return true;
    }

    public TValue GetValueOrDefault(TKey key)
        => TryGetValue(key, out var value) ? value : default!;

    public bool TryAdd(TKey key, TValue value)
    {
        var pair = KeyValuePair.Create(key, value);
        if (_dictionary.TryGetValue(key, out var node))
            return false;

        node = _list.AddFirst(pair);
        _dictionary.Add(key, node);
        Trim();
        return true;
    }

    public void Add(TKey key, TValue value)
    {
        if (!TryAdd(key, value))
            throw new ArgumentException($"The same key already exists in the {GetType().Name}.");
    }

    public bool Remove(TKey key)
    {
        if (!_dictionary.TryGetValue(key, out var node))
            return false;
        _dictionary.Remove(key);
        _list.Remove(node);
        return true;
    }

    public void Clear()
    {
        _dictionary.Clear();
        _list.Clear();
    }

    // Private methods

    private void PopUp(LinkedListNode<KeyValuePair<TKey, TValue>> node)
    {
        _list.Remove(node);
        _list.AddFirst(node);
    }

    private void Trim()
    {
        while (_list.Count > Capacity) {
            var node = _list.Last!;
            _list.Remove(node);
            _dictionary.Remove(node.Value.Key);
        }
    }
}
