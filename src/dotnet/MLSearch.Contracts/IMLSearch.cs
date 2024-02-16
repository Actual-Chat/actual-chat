namespace ActualChat.MLSearch;

public interface IMLSearch
{
    // Commands

    [CommandHandler]
    Task<MLSearchChat> OnCreate(MLSearch_CreateChat command, CancellationToken cancellationToken);
}
