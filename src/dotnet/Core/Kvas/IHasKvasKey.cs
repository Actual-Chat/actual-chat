namespace ActualChat.Kvas;

/// <summary>
/// Interface for settings types that define their own KVAS storage key.
/// </summary>
public interface IHasKvasKey<T> where T : IHasKvasKey<T>
{
    static virtual string KvasKey => typeof(T).Name;
}
