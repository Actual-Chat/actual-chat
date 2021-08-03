using System;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace ActualChat.Db
{
    public sealed class UlidValueGenerator : ValueGenerator<Ulid>
    {
        public override Ulid Next(EntityEntry entry) => Ulid.NewUlid();

        public override bool GeneratesTemporaryValues => false;
    }
}