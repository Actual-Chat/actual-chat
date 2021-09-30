using System.Text;

namespace ActualChat.Mathematics;

// TODO: AK - Remove unused!
public sealed class WordSuffixTree<TValue>
{
    private readonly Node<TValue> _root;

    private Node<TValue> _activeLeaf;

    public WordSuffixTree()
    {
        _root = new Node<TValue>();
        _activeLeaf = _root;
    }

    public void Add(string key, TValue value)
    {
        if (key.IsNullOrEmpty())
            return;

        // reset activeLeaf
        _activeLeaf = _root;

        var remainder = new LinkedList<string>(key.Split());
        var prefix = new LinkedList<string>();
        var node = _root;

        while (remainder.Count > 0) {
            prefix.AddLast(remainder.First());
            remainder.RemoveFirst();
            var (foundNode, subKey) = Update(node, prefix, remainder.ToList(), value);
            var (nextNode, _) = TraverseDeeper(foundNode, subKey);
            node = nextNode;
        }

        if (_activeLeaf.Suffix == null && _activeLeaf != _root && _activeLeaf != node) _activeLeaf.Suffix = node;
    }

    private static (Node<TValue>, IReadOnlyList<string>) TraverseDeeper(Node<TValue> node, IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return new (node, path);

        var currentNode = node;
        // descend the tree as long as a proper label is found
        for (int i = 0; i < path.Count; i++) {
            var word = path[i];
            var edge = currentNode.GetEdge(word);
            if (edge == null)
                return new(currentNode, path.Skip(i).ToList());
            if (StartsWith(path, edge.Label, i)) {
                i += edge.Label.Count;
                currentNode = edge.Target;
            }
            else
                break;
        }

        return new (currentNode, path);
    }

    private (Node<TValue>, IReadOnlyList<string>) Update(
        Node<TValue> inputNode,
        LinkedList<string> path,
        IReadOnlyList<string> rest,
        TValue value)
    {
        var node = inputNode;
        var subPath = path.ToList();
        var word = path.Last!.Value;

        var oldRoot = _root;

        var (endpoint, subTree) = TestAndSplit(node, subPath.Take(subPath.Count - 1).ToList(), word, rest, value);
        while (!endpoint) {
            var tempEdge = subTree.GetEdge(word);
            Node<TValue> leaf;
            if (null != tempEdge)
                leaf = tempEdge.Target;
            else {
                leaf = new Node<TValue>();
                leaf.AddRef(value);
                var newEdge = new Edge<TValue>(rest.ToList(), leaf);
                subTree.AddEdge(word, newEdge);
            }

            if (_activeLeaf != _root) _activeLeaf.Suffix = leaf;
            _activeLeaf = leaf;

            if (oldRoot != _root) oldRoot.Suffix = subTree;

            oldRoot = subTree;

            if (node.Suffix == null)
                subPath = subPath.Skip(1).ToList();
            else {
                var (foundNode, subKey) = TraverseDeeper(node.Suffix, subPath.Take(subPath.Count - 1).ToList());
                node = foundNode;
                subPath = subKey.Concat(Enumerable.Repeat(subPath.Last(), 1)).ToList();
            }

            (endpoint, subTree) = TestAndSplit(node, subPath.Take(subPath.Count - 1).ToList(), word, rest.ToList(), value);
        }

        if (oldRoot != _root) oldRoot.Suffix = subTree;

        return new (node, subPath);
    }

    private static (bool, Node<TValue>) TestAndSplit(
        Node<TValue> inputs,
        IReadOnlyList<string> prefix,
        string word,
        IReadOnlyList<string> remainder,
        TValue value)
    {
        // descend the tree as far as possible
        var (foundNode, subKey) = TraverseDeeper(inputs, prefix);
        if (subKey.Count > 0) {
            var edge = foundNode.GetEdge(subKey[0]);

            var label = edge!.Label;
            if (label.Count > subKey.Count && label[subKey.Count] == word)
                return new (true, foundNode);

            var newLabel = label.Skip(subKey.Count).ToList();

            var node = new Node<TValue>();
            var nEdge = new Edge<TValue>(subKey, node);

            edge.Label = newLabel;

            // link s -> r
            node.AddEdge(newLabel[0], edge);
            foundNode.AddEdge(subKey[0], nEdge);

            return new(false, node);
        }

        var wEdge = foundNode.GetEdge(word);
        if (wEdge == null)
            return (false, foundNode);

        if (remainder.Count == wEdge.Label.Count && StartsWith(remainder, wEdge.Label)) {
            // update payload of destination NodeA<T>
            wEdge.Target.AddRef(value);
            return (true, foundNode);
        }

        if (StartsWith(remainder, wEdge.Label)) return new (true, foundNode);
        if (!StartsWith(wEdge.Label, remainder)) return new (true, foundNode);
        // need to split as above
        var newNode = new Node<TValue>();
        newNode.AddRef(value);

        var newEdge = new Edge<TValue>(remainder, newNode);
        wEdge.Label = wEdge.Label.Skip(remainder.Count).ToList();
        newNode.AddEdge(wEdge.Label[0], wEdge);
        foundNode.AddEdge(word, newEdge);
        return new (false, foundNode);
    }

    private static bool StartsWith(IReadOnlyCollection<string> target, IReadOnlyCollection<string> pattern, int offset = 0) =>
        pattern.Zip(target.Skip(offset)).All(x => x.First == x.Second);


    internal sealed class Node<T>
    {
        private readonly HashSet<T> _data;
        private readonly IDictionary<string, Edge<T>> _edges;

        public Node()
        {
            _data = new HashSet<T>();
            _edges = new Dictionary<string, Edge<T>>();
            Suffix = null;
        }

        public Node<T>? Suffix { get; set; }


        public IEnumerable<T> GetData()
        {
            var childData = _edges.Values.Select(e => e.Target).SelectMany(t => t.GetData());
            return _data.Concat(childData).Distinct();
        }

        public void AddRef(T value)
        {
            if (_data.Contains(value))
                return;

            _data.Add(value);
            //  add this reference to all the suffixes as well
            var iter = Suffix;
            while (iter != null) {
                if (iter._data.Contains(value))
                    break;

                iter.AddRef(value);
                iter = iter.Suffix;
            }
        }

        public void AddEdge(string word, Edge<T> e) => _edges[word] = e;

        public Edge<T>? GetEdge(string word)
        {
            _edges.TryGetValue(word, out var result);
            return result;
        }

        public override string ToString()
        {
            var formattedSuffix = (Suffix?.ToString() ?? "").Replace(Environment.NewLine, Environment.NewLine + "  ");
            var formattedEdges = new StringBuilder();
            foreach (var edge in _edges) formattedEdges.AppendLine($"  {edge}");
            return $"Edges: {formattedEdges}, {nameof(Suffix)}: {formattedSuffix}";
        }
    }

    internal sealed class Edge<T>
    {
        public Edge(IReadOnlyList<string> label, Node<T> target)
        {
            Label = label;
            Target = target;
        }

        public IReadOnlyList<string> Label { get; set; }

        public Node<T> Target { get; }

        public override string ToString() => $"{nameof(Label)}: {Label}, {nameof(Target)}: {Target}";
    }
}
