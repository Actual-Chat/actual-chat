namespace ActualChat.Blobs;

public interface IBlobStorages
{
    IBlobStorage this[Symbol blobScope] { get; }
}
