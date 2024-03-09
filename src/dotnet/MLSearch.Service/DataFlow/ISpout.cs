namespace ActualChat.MLSearch.DataFlow;

internal interface ISpout<in TInput>
{
    Task PostAsync(TInput input, CancellationToken cancellationToken);
}
