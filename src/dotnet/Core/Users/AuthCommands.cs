using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record Auth_EditUser(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string? Name
) : ISessionCommand<Unit>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record Auth_SignOut: ISessionCommand<Unit>
{
    [DataMember, MemoryPackOrder(0)]
    public Session Session { get; init; }
    [DataMember, MemoryPackOrder(1)]
    public string? KickUserSessionHash { get; init; }
    [DataMember, MemoryPackOrder(2)]
    public bool KickAllUserSessions { get; init; }
    [DataMember, MemoryPackOrder(3)]
    public bool Force { get; init; }

    public Auth_SignOut(Session session, bool force = false)
    {
        Session = session;
        Force = force;
    }

    public Auth_SignOut(Session session, string kickUserSessionHash, bool force = false)
    {
        Session = session;
        KickUserSessionHash = kickUserSessionHash;
        Force = force;
    }

    // ReSharper disable once ConvertToPrimaryConstructor
    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public Auth_SignOut(
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
