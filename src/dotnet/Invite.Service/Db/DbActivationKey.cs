using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Generators;

namespace ActualChat.Invite.Db;

[Table("InviteActivationKeys")]
public class DbActivationKey : IHasId<string>, IRequirementTarget
{
    private static RandomStringGenerator SuffixGenerator { get; } = new(10, Alphabet.AlphaNumeric);

    public DbActivationKey() { }
    public DbActivationKey(Symbol inviteId)
        => Id = GenerateId(inviteId);

    [Key] public string Id { get; set; } = null!;

    public static string GenerateId(Symbol inviteId)
        => ComposeId(inviteId, SuffixGenerator.Next());
    public static string ComposeId(Symbol inviteId, string suffix)
        => $"{inviteId}-{suffix}";
}
