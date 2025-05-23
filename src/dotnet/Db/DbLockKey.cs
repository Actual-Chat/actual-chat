namespace ActualChat.Db;

public record struct DbLockKey(string LockSpace, int Key)
{
    public long CombinedKey => ((long)(LockSpace.GetOrdinalHashCode() & 0x0FFFFFFF) << 32) ^ Key;

    public static DbLockKey New(string lockSpace)
        => new(lockSpace, 0);
    public static DbLockKey New<TArg0>(string lockSpace, TArg0 arg0)
        => new(lockSpace, HashCode.Combine(arg0));
    public static DbLockKey New<TArg0, TArg1>(string lockSpace, TArg0 arg0, TArg1 arg1)
        => new(lockSpace, HashCode.Combine(arg0, arg1));
    public static DbLockKey New<TArg0, TArg1, TArg2>(string lockSpace, TArg0 arg0, TArg1 arg1, TArg2 arg2)
        => new(lockSpace, HashCode.Combine(arg0, arg1));

    public static DbLockKey New(Type lockSpace)
        => new(lockSpace.ToIdentifierSymbol(), 0);
    public static DbLockKey New<TArg0>(Type lockSpace, TArg0 arg0)
        => new(lockSpace.ToIdentifierSymbol(), HashCode.Combine(arg0));
    public static DbLockKey New<TArg0, TArg1>(Type lockSpace, TArg0 arg0, TArg1 arg1)
        => new(lockSpace.ToIdentifierSymbol(), HashCode.Combine(arg0, arg1));
    public static DbLockKey New<TArg0, TArg1, TArg2>(Type lockSpace, TArg0 arg0, TArg1 arg1, TArg2 arg2)
        => new(lockSpace.ToIdentifierSymbol(), HashCode.Combine(arg0, arg1));

    public override string ToString()
        => $"('{LockSpace}', {Key})";
}
