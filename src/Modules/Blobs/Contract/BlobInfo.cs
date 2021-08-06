using Stl.Time;

namespace ActualChat.Blobs
{
    public record BlobInfo
    {
        public string Id { get; init; } = "";
        public long Length { get; init; }
        public Moment CreatedAt { get; init; }
        public Moment ModifiedAt { get; init; }
    }
}
