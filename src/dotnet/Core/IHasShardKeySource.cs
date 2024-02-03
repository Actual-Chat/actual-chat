namespace ActualChat;

public interface IHasShardKeySource<out T>
{
    T GetShardKeySource();
}
