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

        public Task<Transcript> GetTranscript(Symbol transcriptId, CancellationToken cancellationToken = default) => throw new System.NotImplementedException();

        public Task<TranscriptSummary> GetSummary(Symbol transcriptId, CancellationToken cancellationToken = default) => throw new System.NotImplementedException();

        public Task<TranscriptAudioSummary> GetAudioSummary(Symbol transcriptId, CancellationToken cancellationToken = default) => throw new System.NotImplementedException();

        public Task<ImmutableArray<TranscriptFragment>> GetFragments(Symbol transcriptId, PageRef<int> page, CancellationToken cancellationToken = default) => throw new System.NotImplementedException();
    }
}