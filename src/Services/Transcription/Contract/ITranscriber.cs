using System;
using System.Collections.Immutable;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Stl.CommandR;
using Stl.Fusion;
using Stl.Fusion.Extensions;
using Stl.Text;

namespace ActualChat.Transcription
{
    public interface ITranscriber
    {
        Task<Symbol> BeginTranscription(BeginTranscriptionCommand command, CancellationToken cancellationToken = default);
        Task AppendTranscription(AppendTranscriptionCommand command, CancellationToken cancellationToken = default);
        Task EndTranscription(EndTranscriptionCommand command, CancellationToken cancellationToken = default);

        Task<PollResult> PollTranscription(PollTranscriptionCommand command, CancellationToken cancellationToken = default);
        
        // probably we should combine Poll and Ack into one method to reduce chattiness
        Task AckTranscription(AckTranscriptionCommand command, CancellationToken cancellationToken = default);
    }
}
