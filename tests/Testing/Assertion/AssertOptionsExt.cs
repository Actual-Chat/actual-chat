using FluentAssertions.Equivalency;
using ActualLab.Versioning;

namespace ActualChat.Testing.Assertion;

public static class AssertOptionsExt
{
    public static EquivalencyAssertionOptions<T> ExcludingSystemProperties<T>(
        this EquivalencyAssertionOptions<T> options) where T : notnull
        => options.Excluding(mi => OrdinalEquals(mi.Name, nameof(IHasVersion<T>.Version)))
            .Excluding(mi => OrdinalEquals(mi.Name, "CreatedAt"))
            .Excluding(mi => OrdinalEquals(mi.Name, "ModifiedAt"));
}
