namespace ActualChat.Kvas;

public interface IOriginProvider
{
    Task WhenReady { get; }
    string Origin { get; }
}
