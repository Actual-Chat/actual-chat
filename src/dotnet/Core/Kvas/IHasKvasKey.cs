namespace ActualChat.Kvas;

public interface IHasKvasKey<T> where T : IHasKvasKey<T>
{
    static virtual string KvasKey => typeof(T).Name;
}
