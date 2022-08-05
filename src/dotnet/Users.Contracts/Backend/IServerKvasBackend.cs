namespace ActualChat.Users;

public interface IServerKvasBackend
{
    [ComputeMethod]
    Task<string?> Get(string prefix, string key, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task SetMany(SetManyCommand command, CancellationToken cancellationToken = default);

    [DataContract]
    public record SetManyCommand(
        [property: DataMember(Order = 0)] string Prefix,
        [property: DataMember(Order = 1)] (string Key, string? Value)[] Items
        ) : ICommand<Unit>;
}
