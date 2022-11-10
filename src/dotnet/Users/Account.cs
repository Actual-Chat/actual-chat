namespace ActualChat.Users;

[DataContract]
public record Account(
    [property: DataMember] Symbol Id
) : IHasId<Symbol>, IRequirementTarget
{
    public static Account None => AccountFull.None;
    public static Account Loading => AccountFull.Loading; // Should differ by ref. from None

    public static Requirement<Account> MustExist { get; } = Requirement.New(
        new(() => StandardError.Account.None()),
        (Account? a) => a is { Id.IsEmpty: false });

    [DataMember] public long Version { get; init; }
    [DataMember] public AccountStatus Status { get; init; }
    [DataMember] public Avatar Avatar { get; init; } = null!; // Populated only on reads
}
