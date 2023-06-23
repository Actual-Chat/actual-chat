namespace ActualChat;

public interface ICloneable<out T> : ICloneable
{
    new T Clone();
}
