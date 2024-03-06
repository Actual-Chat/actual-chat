using ActualChat.Users;
using FluentAssertions.Equivalency;

namespace ActualChat.Testing.Host;

public static class AssertionExt
{
    public static EquivalencyAssertionOptions<AccountFull> IdName(
        this EquivalencyAssertionOptions<AccountFull> options)
        => options.Including(x => x.Id).Including(x => x.FullName).Including(x => x.User.Name);
}
