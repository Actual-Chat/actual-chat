using ActualChat.Hashing;
using MemoryPack;

namespace ActualChat.Contacts;

public sealed class ExternalContactHasher
{
    private IByteSerializer ByteSerializer { get; } = MemoryPackByteSerializer.Default;

    public HashString Compute(ExternalContactFull externalContactFull)
    {
 #pragma warning disable IL2026
        using var buffer = ByteSerializer.Write(HashedExternalContact.From(externalContactFull));
 #pragma warning restore IL2026
        return buffer.WrittenSpan.Hash().SHA256().ToBase64HashString(HashAlgorithm.SHA256);
    }

    public HashString Compute(IEnumerable<ExternalContactFull> deviceContacts)
        => deviceContacts.Select(x => (HashOutput32)x.WithHash(this, false).Hash.ToHashOutput())
            .BitwiseXor()
            .ToBase64HashString(HashAlgorithm.SHA256Xor);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial record HashedExternalContact
{
    [DataMember, MemoryPackOrder(0)] public ExternalContactId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public string DisplayName { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public string GivenName { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public string FamilyName { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public string MiddleName { get; init; } = "";
    [DataMember, MemoryPackOrder(5)] public string NamePrefix { get; init; } = "";
    [DataMember, MemoryPackOrder(6)] public string NameSuffix { get; init; } = "";
    [DataMember, MemoryPackOrder(7)] public ApiSet<string> PhoneHashes { get; init; } = ApiSet<string>.Empty;
    [DataMember, MemoryPackOrder(8)] public ApiSet<string> EmailHashes { get; init; } = ApiSet<string>.Empty;

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
