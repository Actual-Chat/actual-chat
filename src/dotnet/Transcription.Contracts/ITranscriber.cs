namespace ActualChat.Transcription;

public interface ITranscriber
{
    Task<Symbol> BeginTranscription(BeginTranscriptionCommand command, CancellationToken cancellationToken);
    Task AppendTranscription(AppendTranscriptionCommand command, CancellationToken cancellationToken);
    Task EndTranscription(EndTranscriptionCommand command, CancellationToken cancellationToken);

    // TODO(AY): Make this [ComputeMethod] GetTranscription?
    Task<PollResult> PollTranscription(PollTranscriptionCommand command, CancellationToken cancellationToken);

    // TODO(AK): Combine Poll and Ack into one method to reduce chattiness?
    Task AckTranscription(AckTranscriptionCommand command, CancellationToken cancellationToken);
}
