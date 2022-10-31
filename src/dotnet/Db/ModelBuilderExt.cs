using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ActualChat.Db;

public static class ModelBuilderExt
{
    public static void UseSnakeCaseNaming(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes()) {
            var tableName = FixName(entity.GetTableName());
            if (tableName != null) {
                entity.SetTableName(tableName);

                var schema = entity.GetSchema();
                var tableIdentifier = StoreObjectIdentifier.Table(tableName, schema);
                foreach (var property in entity.GetProperties()) {
                    var columnName = property.GetColumnName(tableIdentifier);
                    if (columnName != null)
                        property.SetColumnName(FixName(columnName));
                }
            }

            foreach (var key in entity.GetKeys()) {
                var keyName = FixName(key.GetName());
                if (keyName != null)
                    key.SetName(keyName);
            }

            foreach (var foreignKey in entity.GetForeignKeys()) {
                var constraintName = FixName(foreignKey.GetConstraintName());
                if (constraintName != null)
                    foreignKey.SetConstraintName(constraintName);
            }

            foreach (var index in entity.GetIndexes()) {
                var indexName = FixName(index.GetDatabaseName());
                index.SetDatabaseName(indexName);
            }
        }
    }

    [return: NotNullIfNotNull("name")]
    private static string? FixName(string? name)
    {
        if (name == null)
            return null;

        if (name.OrdinalStartsWith("Db") && name.Length > 2)
            name = name[2..];
        name = name.ToSnakeCase();
        return name;
    }
}
