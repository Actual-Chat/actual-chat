namespace ActualChat;

public interface ICanBeNone
{
    bool IsNone { get; }
}

public interface ICanBeNone<out TSelf>
    where TSelf : ICanBeNone<TSelf>
{
    public static abstract TSelf None { get; }
}
