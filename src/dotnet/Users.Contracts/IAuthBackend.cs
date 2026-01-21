using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Users;

public interface IAuthBackend : IComputeService, IBackendService
{
    // Commands
    [CommandHandler]
    Task OnSignIn(AuthBackend_SignIn command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task<SessionInfo> OnSetupSession(AuthBackend_SetupSession command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnSetOptions(AuthBackend_SetSessionOptions command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnSignOut(AuthBackend_SignOut command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnEditUser(AuthBackend_EditUser command, CancellationToken cancellationToken = default);

    // Queries
    [ComputeMethod(MinCacheDuration = 10)]
    Task<SessionInfo?> GetSessionInfo(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<SessionAuthInfo?> GetAuthInfo(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<bool> IsSignOutForced(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<ImmutableArray<SessionInfo>> GetUserSessions(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<User?> GetUser(string userId, CancellationToken cancellationToken = default);

    // For UpdatePresence - non-compute
    Task UpdatePresence(Session session, CancellationToken cancellationToken = default);
}

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

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record AuthBackend_SignOut : ISessionCommand<Unit>, IBackendCommand
{
    [DataMember, MemoryPackOrder(0)]
    public Session Session { get; init; }
    [DataMember, MemoryPackOrder(1)]
    public string? KickUserSessionHash { get; init; }
    [DataMember, MemoryPackOrder(2)]
    public bool KickAllUserSessions { get; init; }
    [DataMember, MemoryPackOrder(3)]
    public bool Force { get; init; }

    public AuthBackend_SignOut(Session session, bool force = false)
    {
        Session = session;
        Force = force;
    }

    public AuthBackend_SignOut(Session session, string kickUserSessionHash, bool force = false)
    {
        Session = session;
        KickUserSessionHash = kickUserSessionHash;
        Force = force;
    }

    // ReSharper disable once ConvertToPrimaryConstructor
    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public AuthBackend_SignOut(
        Session session,
        string? kickUserSessionHash,
        bool kickAllUserSessions,
        bool force)
    {
        Session = session;
        KickUserSessionHash = kickUserSessionHash;
        KickAllUserSessions = kickAllUserSessions;
        Force = force;
    }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record AuthBackend_EditUser(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string? Name
) : ISessionCommand<Unit>, IBackendCommand;
