namespace ActualChat;

public interface IHasShardKey<out T>
{
    T GetShardKey();
}
