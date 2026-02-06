namespace ActualChat.Blobs;

/// <summary>
/// Factory for accessing <see cref="IBlobStorage"/> instances by scope.
/// </summary>
public interface IBlobStorages
{
    IBlobStorage this[Symbol blobScope] { get; }
}
