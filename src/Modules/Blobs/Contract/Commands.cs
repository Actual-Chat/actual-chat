using System.Reactive;
using Stl.CommandR.Commands;
using Stl.Serialization;

namespace ActualChat.Blobs
{
    public record BlobAppendCommand(string BlobId, Base64Encoded Data) : ServerSideCommandBase<Unit>
    {
        public BlobAppendCommand() : this("", default) { }
    }

    public record BlobRemoveCommand(string BlobId) : ServerSideCommandBase<Unit>
    {
        public BlobRemoveCommand() : this("") { }
    }
}
