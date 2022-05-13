using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ActualChat.Db;

public static class ModelBuilderExtensions
{
    public static void UseSnakeCaseNaming(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(entity.GetTableName().ToSnakeCase());

            foreach (var property in entity.GetProperties()) {
                var identifier = StoreObjectIdentifier.Table(entity.GetTableName(), entity.GetSchema());
                var columnName = property.GetColumnName(identifier);
                property.SetColumnName(columnName.ToSnakeCase());
            }

            foreach (var key in entity.GetKeys())
                key.SetName(key.GetName().ToSnakeCase());

            foreach (var key in entity.GetForeignKeys())
                key.SetConstraintName(key.GetConstraintName().ToSnakeCase());

            foreach (var index in entity.GetIndexes())
                index.SetDatabaseName(index.GetDatabaseName().ToSnakeCase());
        }
    }
}
