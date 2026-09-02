namespace ActualChat.Chat;

// A scope lives for one parse: ParseRaw opens the outermost one, and every nested parse of a
// paragraph, header, quote or table cell opens its own, since positions in its text mean nothing
// in the enclosing one. A parse is synchronous and runs on one thread, so the scopes are
// thread-static and pooled per thread - a warm thread allocates nothing here per parse.

/// <summary>
/// The per-parse tables behind <see cref="MemoParser"/>: one per parser, keyed by input position.
/// </summary>
internal static class ParseMemo
{
    private static int _parserCount;
    [ThreadStatic]
    private static Scope? _current;
    [ThreadStatic]
    private static Stack<Scope>? _pool;

    public static int NextParserIndex()
        => Interlocked.Increment(ref _parserCount) - 1;

    public static void Push()
    {
        var pool = _pool ??= new Stack<Scope>();
        var scope = pool.Count > 0 ? pool.Pop() : new Scope();
        scope.Parent = _current;
        _current = scope;
    }

    public static void Pop()
    {
        var scope = _current!;
        _current = scope.Parent;
        scope.Clear();
        _pool!.Push(scope);
    }

    // Null outside of a scope, which makes a MemoParser used there a pass-through
    public static Dictionary<long, (Markup? Result, int Length)>? TryGetTable(int parserIndex)
        => _current?.GetTable(parserIndex);

    // Nested types

    private sealed class Scope
    {
        private Dictionary<long, (Markup? Result, int Length)>?[] _tables = [];

        public Scope? Parent { get; set; }

        public Dictionary<long, (Markup? Result, int Length)> GetTable(int parserIndex)
        {
            if (parserIndex >= _tables.Length)
                Array.Resize(ref _tables, Math.Max(parserIndex + 1, 2 * _tables.Length));

            return _tables[parserIndex] ??= new Dictionary<long, (Markup? Result, int Length)>();
        }

        public void Clear()
        {
            foreach (var table in _tables)
                table?.Clear();
            Parent = null;
        }
    }
}
