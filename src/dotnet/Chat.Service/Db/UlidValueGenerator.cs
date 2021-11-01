using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace ActualChat.Chat.Db;

/// <summary>
/// Returns new Ulid value for Id database fields.
/// </summary>
internal class UlidValueGenerator : ValueGenerator<string>
{
    /// <inheritdoc cref="StringValueGenerator.GeneratesTemporaryValues"/>
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    public override string Next(EntityEntry entry) => Ulid.NewUlid().ToString();
}
