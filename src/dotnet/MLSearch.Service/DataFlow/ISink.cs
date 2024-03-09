namespace ActualChat.MLSearch.DataFlow;

internal interface ISink<in TInput>
{
    Task ExecuteAsync(TInput input, CancellationToken cancellationToken);
}
