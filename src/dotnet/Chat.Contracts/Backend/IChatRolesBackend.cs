namespace ActualChat.Chat;

public interface IChatRolesBackend : IComputeService
{
    [ComputeMethod]
    Task<ChatRole?> Get(string chatId, string roleId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListRoleIds(string chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListRoleIds(string chatId, string authorId, CancellationToken cancellationToken);

    [CommandHandler]
    Task Upsert(UpsertCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpsertCommand(
        [property: DataMember] string ChatId,
        [property: DataMember] string RoleId
    ) : ICommand<Unit>
    {
        [DataMember] public string? Title { get; init; }
        [DataMember] public string[] AddAuthorIds { get; init; } = Array.Empty<string>();
        [DataMember] public string[] RemoveAuthorIds { get; init; } = Array.Empty<string>();
        [DataMember] public bool MustRemove { get; init; }
    }
}
