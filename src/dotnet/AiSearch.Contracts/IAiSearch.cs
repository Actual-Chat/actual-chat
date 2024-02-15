namespace ActualChat.AiSearch;

public interface IAiSearch
{
    // Commands

    [CommandHandler]
    Task<AiSearchChat> OnCreate(AiSearch_CreateChat command, CancellationToken cancellationToken);
}
