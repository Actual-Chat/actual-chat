namespace ActualChat.Blobs;

/// <summary>
/// Represents content to be stored with its ID, MIME type, and data stream.
/// </summary>
public record Content(string ContentId, string ContentType, Stream Stream);
