namespace ActualChat.MLSearch.DataFlow;

internal interface ITransform<in TInput, TOutput>
{
    Task<TOutput> ExecuteAsync(TInput input, CancellationToken cancellationToken);
}
