using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record AuthBackend_SetSessionOptions(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ImmutableOptionSet Options,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion = null
) : ISessionCommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
// ReSharper disable once InconsistentNaming
public partial record AuthBackend_SetupSession(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string IPAddress,
    [property: DataMember, MemoryPackOrder(2)] string UserAgent,
    [property: DataMember, MemoryPackOrder(3)] ImmutableOptionSet Options
) : ISessionCommand<SessionInfo>, IBackendCommand, INotLogged
{
    public AuthBackend_SetupSession(Session session, string ipAddress = "", string userAgent = "")
        : this(session, ipAddress, userAgent, default) { }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
// ReSharper disable once InconsistentNaming
public partial record AuthBackend_SignIn(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] User User,
    [property: DataMember, MemoryPackOrder(2)] UserIdentity AuthenticatedIdentity
) : ISessionCommand<Unit>, IBackendCommand
{
    public AuthBackend_SignIn(Session session, User user)
        : this(session, user, user.Identities.Single().Key) { }
}
