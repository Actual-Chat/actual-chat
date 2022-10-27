namespace ActualChat.Users;

public interface IServerKvasBackend : IComputeService
{
    [ComputeMethod]
    Task<string?> Get(string prefix, string key, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<ImmutableList<(string Key, string Value)>> List(string prefix, CancellationToken cancellationToken = default);

    string GetUserPrefix(string userId);
    string GetSessionPrefix(Session session);

    [CommandHandler]
    Task SetMany(SetManyCommand command, CancellationToken cancellationToken = default);

    [DataContract]
    public record SetManyCommand(
        [property: DataMember(Order = 0)] string Prefix,
        [property: DataMember(Order = 1)] params (string Key, string? Value)[] Items
    ) : ICommand<Unit>, IBackendCommand;
}
