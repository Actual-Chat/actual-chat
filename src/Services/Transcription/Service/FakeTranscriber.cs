using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion.Extensions;
using Stl.Text;

namespace ActualChat.Transcription
{
    public class FakeTranscriber : ITranscriber
    {
        public Task<Symbol> BeginTranscription(BeginTranscriptionCommand command, CancellationToken cancellationToken = default) => Task.FromResult((Symbol)Ulid.NewUlid().ToString());

        public Task AppendTranscription(AppendTranscriptionCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EndTranscription(EndTranscriptionCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ImmutableArray<TranscriptFragmentVariant>> PollTranscription(PollTranscriptionCommand command, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task AckTranscription(AckTranscriptionCommand command, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}