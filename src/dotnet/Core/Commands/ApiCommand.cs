using ActualLab.Generators;

namespace ActualChat;

/// <summary>
/// Base record for client-callable API commands: carries a client-generated
/// <see cref="Uuid"/> (idempotency key, auto-generated so callers never pass it) and the <see cref="Session"/>.
/// </summary>
[DataContract, MessagePackObject]
public partial record ApiCommand : ISessionCommand, IApiCommand, IHasUuid
{
    [DataMember(Order = 0), Key(0)] public string Uuid { get; init; } = NewUuid();
    [DataMember(Order = 1), Key(1)] public required Session Session { get; init; }

    // ReSharper disable once MemberCanBePrivate.Global
    public static Func<string> NewUuidGenerator { get; set; }
        = () => RandomStringGenerator.Default.Next();

    public static string NewUuid()
        => NewUuidGenerator();
}

/// <summary>
/// Strongly-typed <see cref="ApiCommand"/> returning a result of type <typeparamref name="TResult"/>.
/// </summary>
public abstract partial record ApiCommand<TResult> : ApiCommand, ISessionCommand<TResult>;

/// <summary>
/// Opts a command out of <c>ApiCommandDeduplicator</c>: high-frequency commands whose repeat is
/// harmless but whose suppression isn't, and which never resend the same <see cref="ApiCommand.Uuid"/>,
/// pay the dedup store's two round-trips for nothing.
/// </summary>
public interface INotDeduplicated;
