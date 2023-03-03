namespace ActualChat;

public static class Delegates
{
    public static readonly Action Noop = () => { };
}

public static class Delegates<T>
{
    public static readonly Func<T, T> Identity = static x => x;
    public static readonly Func<T, bool> AlwaysTrue = static x => true;
    public static readonly Func<T, bool> AlwaysFalse = static x => false;
}
