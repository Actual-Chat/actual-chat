using System.Text;
using Microsoft.EntityFrameworkCore.Update;
using Base_NpgsqlUpdateSqlGenerator = Npgsql.EntityFrameworkCore.PostgreSQL.Update.Internal.NpgsqlUpdateSqlGenerator;

namespace ActualChat.Db.Npgsql;

#pragma warning disable EF1001

public class NpgsqlUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
    : Base_NpgsqlUpdateSqlGenerator(dependencies)
{
    private static readonly ConcurrentDictionary<Type, ConflictStrategy> ConflictStrategies = new();

    protected override void AppendInsertCommand(
        StringBuilder commandStringBuilder,
        string name,
        string? schema,
        IReadOnlyList<IColumnModification> writeOperations,
        IReadOnlyList<IColumnModification> readOperations,
        bool overridingSystemValue)
    {
        AppendInsertCommandHeader(commandStringBuilder, name, schema, writeOperations);

        if (overridingSystemValue)
            commandStringBuilder.AppendLine().Append("OVERRIDING SYSTEM VALUE");

        AppendValuesHeader(commandStringBuilder, writeOperations);
        AppendValues(commandStringBuilder, name, schema, writeOperations);

        var operation = writeOperations.Count != 0 ? writeOperations[0] : null;
        if (operation?.Entry != null) {
            var type = operation.Entry.EntityType.ClrType;
            var conflictStrategy = ConflictStrategies.GetOrAdd(
                type,
                static (_, entityType) => {
                    var conflictStrategyAnnotation = entityType.FindAnnotation(nameof(ConflictStrategy));
                    var conflictStrategyValue = conflictStrategyAnnotation?.Value;
                    return conflictStrategyValue != null
                        ? (ConflictStrategy)conflictStrategyValue
                        : ConflictStrategy.Unspecified;
                },
                operation.Entry.EntityType
            );
            if (conflictStrategy != ConflictStrategy.Unspecified)
                AppendConflictClause(commandStringBuilder, writeOperations, conflictStrategy);
        }
        if (readOperations.Count > 0)
            AppendReturningClause(commandStringBuilder, readOperations);
        commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);
    }

    protected virtual void AppendConflictClause(
        StringBuilder commandStringBuilder,
        IReadOnlyList<IColumnModification> writeOperations,
        ConflictStrategy conflictStrategy)
    {
        switch (conflictStrategy) {
        case ConflictStrategy.Unspecified:
        case ConflictStrategy.Update when writeOperations.Count == 0:
            return;
        case ConflictStrategy.DoNothing:
            commandStringBuilder
                .AppendLine()
                .Append("ON CONFLICT DO NOTHING ");
            break;
        default:
            var (keyOperations, updateOperations) = writeOperations.Split(op => op.IsKey);
            commandStringBuilder
                .AppendLine()
                .Append("ON CONFLICT (")
                .AppendJoin(
                    keyOperations,
                    SqlGenerationHelper,
                    (sb, o, helper) => helper.DelimitIdentifier(sb, o.ColumnName))
                .Append(')')
                .AppendLine()
                .Append("DO UPDATE SET ")
                .AppendJoin(
                    updateOperations,
                    SqlGenerationHelper,
                    (sb, o, helper) => {
                        helper.DelimitIdentifier(sb, o.ColumnName);
                        sb.Append(" = EXCLUDED.");
                        helper.DelimitIdentifier(sb, o.ColumnName);
                    });
            break;
        }
    }
}
