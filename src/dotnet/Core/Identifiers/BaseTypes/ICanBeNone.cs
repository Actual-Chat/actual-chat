namespace ActualChat;

public interface ICanBeNone
{
    bool IsNone { get; }
}

#pragma warning disable CA1000

public interface ICanBeNone<out TSelf> : ICanBeNone
    where TSelf : ICanBeNone<TSelf>
{
    public static abstract TSelf None { get; }
}
