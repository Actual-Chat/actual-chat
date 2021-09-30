using System.Collections.Immutable;

namespace ActualChat.Mathematics;

public sealed class  WordPrefixTree<TValue>
{
    private readonly Node<TValue> _root;

    public WordPrefixTree()
    {
        _root = new Node<TValue>();
    }

    public void Add(IReadOnlyList<string> phrase, TValue value)
    {
        if (phrase.Count == 0)
            return;

        var (foundNode, remaining) = TraverseDeeper(_root, phrase);
        if (remaining.Count == 0)
            foundNode.AddData(value);
        else {
            foreach (var word in remaining) {
                var node = new Node<TValue>(foundNode);
                foundNode.AddNode(word, node);
                foundNode = node;
            }
            foundNode.AddData(value);
        }
    }

    public (IReadOnlyList<string>, IReadOnlyList<TValue>) GetCommonPrefix(IReadOnlyList<string> phrase)
    {
        if (phrase.Count == 0)
            return new (ImmutableArray<string>.Empty, default!);

        var (foundNode, remaining) = TraverseDeeper(_root, phrase);
        return new(phrase.Take(phrase.Count - remaining.Count).ToList(), foundNode.GetData().ToList());
    }

    private static (Node<TValue>, IReadOnlyList<string>) TraverseDeeper(Node<TValue> node, IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return new (node, path);

        var currentNode = node;
        int level;
        // descend the tree as long as a proper label is found
        for (level = 0; level < path.Count; level++) {
            var word = path[level];
            var next = currentNode.GetNode(word);
            if (next == null)
                return new(currentNode, path.Skip(level).ToList());
            currentNode = next;
        }

        return new (currentNode, ImmutableList<string>.Empty);
    }

    private sealed class Node<T>
    {
        private readonly Node<T>? _parent;
        private readonly HashSet<T> _data;
        private readonly IDictionary<string, Node<T>> _nodes;

        public Node() : this(null!)
        {
        }

        public Node(Node<T> parent)
        {
            _parent = parent;
            _data = new HashSet<T>();
            _nodes = new Dictionary<string, Node<T>>();
        }

        public IReadOnlyCollection<T> GetData()
        {
            var set = new HashSet<T>(_data);
            var parent = _parent;
            while (parent != null) {
                set.UnionWith(parent._data);
                parent = parent._parent;
            }
            return set;
        }

        public void AddData(T value)
        {
            if (_data.Contains(value))
                return;

            _data.Add(value);
        }

        public void AddNode(string word, Node<T> e) => _nodes[word] = e;

        public Node<T>? GetNode(string word)
        {
            _nodes.TryGetValue(word, out var result);
            return result;
        }
    }
}
