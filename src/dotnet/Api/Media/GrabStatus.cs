using System.Text;
using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Media;

/// <summary>
/// Tracks the status of a link preview grab operation.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
public sealed partial record GrabStatus(
    [property: DataMember, Key(0)] Symbol Id,
    [property: DataMember, Key(1)] long Version = 0)
    : IHasId<Symbol>, IRequirementTarget
{
    [DataMember, Key(2)] public bool IsSuccessful { get; init; }
    [DataMember, Key(3)] public Moment ModifiedAt { get; init; }

    // Computed properties

    public GrabStatus() : this(Symbol.Empty) { }

    // This record relies on referential equality
    public bool Equals(GrabStatus? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public static Symbol ComposeId(string url)
        => url.IsNullOrEmpty()
            ? Symbol.Empty
            : url.Hash(Encoding.UTF8).SHA256().AlphaNumeric();
}
