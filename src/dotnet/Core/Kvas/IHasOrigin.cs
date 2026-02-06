namespace ActualChat.Kvas;

/// <summary>
/// Interface for settings that track their origin (client vs server).
/// </summary>
public interface IHasOrigin
{
    public string Origin { get; }
}
