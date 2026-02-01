namespace ActualChat.Async;

/// <summary>
/// Interface for items that can be multiplexed/demultiplexed over a single channel.
/// </summary>
public interface IMuxable
{
    int StreamIndex { get; set; }
}
