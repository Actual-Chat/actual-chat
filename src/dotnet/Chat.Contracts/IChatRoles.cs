namespace ActualChat.Chat;

public interface IChatRoles
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ImmutableArray<Symbol>> ListOwnRoleIds(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task Upsert(UpsertCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpsertCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] string RoleId
    ) : ISessionCommand<Unit>
    {
        [DataMember] public string? Title { get; init; }
        [DataMember] public string[] AddPrincipalIds { get; init; } = Array.Empty<string>();
        [DataMember] public string[] RemovePrincipalIds { get; init; } = Array.Empty<string>();
        [DataMember] public bool MustRemove { get; init; }
    }
}
