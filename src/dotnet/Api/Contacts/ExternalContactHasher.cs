using ActualChat.Hashing;

namespace ActualChat.Contacts;

/// <summary>
/// Computes SHA256 hashes for external contacts to detect changes.
/// </summary>
public sealed class ExternalContactHasher
{
    private IByteSerializer ByteSerializer { get; } = Serializers.MemoryPack;

    public HashString Compute(ExternalContactFull externalContactFull)
    {
        using var buffer = ByteSerializer.Write(HashedExternalContact.From(externalContactFull));
        return buffer.WrittenSpan.Hash().SHA256().ToBase64HashString(HashAlgorithm.SHA256);
    }

    public HashString Compute(IEnumerable<ExternalContactFull> deviceContacts)
        => deviceContacts.Select(x => (HashOutput32)x.WithHash(this, false).Hash.ToHashOutput())
            .BitwiseXor()
            .ToBase64HashString(HashAlgorithm.SHA256Xor);
}

// IMPORTANT: Do NOT remove MemoryPack support from this type.
// Its hash is content-addressed via MemoryPack bytes (see ExternalContactHasher.Compute).
// Switching the serializer would invalidate every previously stored hash and trigger
// a full re-detection pass for all external contacts.
// This type is backend-only, so the MemoryPack dependency does not leak to clients.
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true, AllowPrivate = true)]
internal sealed partial record HashedExternalContact
{
    [DataMember, MemoryPackOrder(0)] public ExternalContactId Id { get; init; } = null!;
    [DataMember, MemoryPackOrder(1)] public string DisplayName { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public string GivenName { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public string FamilyName { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public string MiddleName { get; init; } = "";
    [DataMember, MemoryPackOrder(5)] public string NamePrefix { get; init; } = "";
    [DataMember, MemoryPackOrder(6)] public string NameSuffix { get; init; } = "";
    [DataMember, MemoryPackOrder(7)] public ApiSet<string> PhoneHashes { get; init; } = new ApiSet<string>();
    [DataMember, MemoryPackOrder(8)] public ApiSet<string> EmailHashes { get; init; } = new ApiSet<string>();

    public static HashedExternalContact From(ExternalContactFull externalContactFull)
        => new () {
            Id = externalContactFull.Id,
            DisplayName = externalContactFull.DisplayName,
            GivenName = externalContactFull.GivenName,
            FamilyName = externalContactFull.FamilyName,
            MiddleName = externalContactFull.MiddleName,
            NamePrefix = externalContactFull.NamePrefix,
            NameSuffix = externalContactFull.NameSuffix,
            PhoneHashes = externalContactFull.PhoneHashes,
            EmailHashes = externalContactFull.EmailHashes,
        };
}
