namespace ActualChat.Users;

[DataContract]
public record Account(
    [property: DataMember] Symbol Id
) : IHasId<Symbol>, IRequirementTarget
{
    public static Account None { get; } = new(Symbol.Empty) { Avatar = Avatar.None };
    public static Account Loading { get; } = new(Symbol.Empty) { Avatar = Avatar.Loading }; // Should differ by ref. from None

    public static Requirement<Account> MustExist { get; } = Requirement.New(
        new(() => StandardError.Account.None()),
        (Account? a) => a is { Id.IsEmpty: false });

    [DataMember] public long Version { get; init; }
    [DataMember] public AccountStatus Status { get; init; }
    [DataMember] public Avatar Avatar { get; init; } = null!; // Populated only on reads
}
