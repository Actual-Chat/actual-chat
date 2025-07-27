namespace ActualChat.UI.Blazor.App.Services;

using System;
using System.Collections;
using System.Collections.Generic;

public class WeakValueTable<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : notnull
    where TValue : class
{
    private readonly Dictionary<TKey, WeakReference<TValue>> _inner = new ();
    private int _cleanupIndex;

    public Lock SyncObject { get; } = new ();

    public int Count {
        get {
            Cleanup(true);
            return _inner.Count;
        }
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        Cleanup(true);
        foreach (var kv in _inner)
            if (kv.Value.TryGetTarget(out var value))
                yield return new KeyValuePair<TKey, TValue>(kv.Key, value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void AddOrUpdate(TKey key, TValue value)
    {
        Cleanup(false);
        _inner[key] = new WeakReference<TValue>(value);
    }

    public bool TryGetValue(TKey key, out TValue? value)
    {
        Cleanup(false);

        if (_inner.TryGetValue(key, out var weakRef)) {
            if (weakRef.TryGetTarget(out value))
                return true;

            _inner.Remove(key);
        }

        value = null!;
        return false;
    }

    public bool Remove(TKey key)
        => _inner.Remove(key);



    private void Cleanup(bool force)
    {
        if (!force) {
            if (_cleanupIndex < 20)
                return;

            _cleanupIndex++;
        }

        List<TKey>? toRemove = null;
        foreach (var kv in _inner) {
            if (kv.Value.TryGetTarget(out _))
                continue;

            toRemove ??= new List<TKey>();
            toRemove.Add(kv.Key);
        }
        if (toRemove is not null)
            foreach (var key in toRemove)
                _inner.Remove(key);

        _cleanupIndex = 0;
    }
}
