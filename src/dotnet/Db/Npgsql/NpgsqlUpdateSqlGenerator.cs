using System.Text;
using Microsoft.EntityFrameworkCore.Update;
using Base_NpgsqlUpdateSqlGenerator = Npgsql.EntityFrameworkCore.PostgreSQL.Update.Internal.NpgsqlUpdateSqlGenerator;

namespace ActualChat.Db.Npgsql;

public class NpgsqlUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
    : Base_NpgsqlUpdateSqlGenerator(dependencies)
{
    private static readonly ConcurrentDictionary<Type, ConflictStrategy?> _conflictResolutionMap = new ();

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

        ConflictStrategy? conflictStrategy = null;
        var operation = writeOperations.FirstOrDefault();
        if (operation?.Entry != null) {
            var type = operation.Entry.EntityType.ClrType;
            conflictStrategy = _conflictResolutionMap.GetOrAdd(
                type,
                static (_, entityType) => {
                    var conflictStrategyAnnotation = entityType.FindAnnotation(nameof(ConflictStrategy));
                    return (ConflictStrategy?)conflictStrategyAnnotation?.Value;
                },
                operation.Entry.EntityType
            );

            if (conflictStrategy != null)
                AppendConflictClause(commandStringBuilder, writeOperations, conflictStrategy.Value);
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

        if (conflictStrategy == ConflictStrategy.DoUpdate && writeOperations.Count == 0)
            return;

        if (conflictStrategy == ConflictStrategy.DoNothing)
            commandStringBuilder
                .AppendLine()
                .Append("ON CONFLICT DO NOTHING ");
        else {
            var (keyOperations, updateOperations) = writeOperations.Split(op => op.IsKey);
            commandStringBuilder
                .AppendLine()
                .Append("ON CONFLICT (")
                .AppendJoin(
                    keyOperations,
                    SqlGenerationHelper,
                    (sb, o, helper) => helper.DelimitIdentifier(sb, o.ColumnName))
                .Append(")")
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
        }
    }
}
