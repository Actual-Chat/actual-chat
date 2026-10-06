using ActualChat.App.Server.Initializers;
using ActualChat.Db.Module;
using ActualChat.Testing.Host;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ActualChat.Core.Server.IntegrationTests.Db;

// Regular test hosts build their databases with EnsureCreated, so this is the only place
// the migration history is applied - and checked against the model EnsureCreated builds from.
[Trait("Category", "Slow")]
public sealed class DbMigrationTest(ITestOutputHelper @out)
    : AppHostTestBase("db-migrated", TestAppHostOptions.None, @out)
{
    private const string UserSchemaFilter = "not in ('pg_catalog', 'information_schema', 'pg_toast')";
    // Migrations retire a table by renaming it to _gc_*, so it stays in a migrated DB only
    private const string LiveTableFilter = @"not like '\_gc\_%'";
    private const string DefaultPrefix = "default ";

    [Fact(Timeout = 180_000)]
    public async Task MigratedSchemaShouldMatchEnsureCreatedSchema()
    {
        // act
        Dictionary<string, string> migratedDbs;
        await using (var migratedHost = await NewAppHost(o => o with {
            ConfigureHost = (_, cfg) => cfg.AddInMemory<DbSettings>((x => x.ShouldRecreateDb, "false")),
        })) {
            // With ShouldRecreateDb off, initializers migrate whatever DB they find - so it must be empty
            migratedDbs = GetConnectionStrings(migratedHost);
            foreach (var connectionString in migratedDbs.Values)
                await DropDb(connectionString);
            await migratedHost.RunInitializers();
        }

        Dictionary<string, string> createdDbs;
        await using (var createdHost = await NewAppHost(o => o.With("db-created", Out) with {
            MustInitializeDb = true,
            DbInitializeOptions = new() { InitializeData = false },
        }))
            createdDbs = GetConnectionStrings(createdHost);

        // assert
        migratedDbs.Should().NotBeEmpty();
        createdDbs.Keys.Should().BeEquivalentTo(migratedDbs.Keys);
        var diffs = new List<string>();
        foreach (var (initializerName, migratedDb) in migratedDbs.OrderBy(kv => kv.Key)) {
            var createdDb = createdDbs[initializerName];
            GetDbName(createdDb).Should().NotBe(GetDbName(migratedDb),
                "a DB name template without {instance_} makes both hosts share one DB, so it is compared to itself");
            var migratedSchema = await GetSchema(migratedDb);
            var createdSchema = await GetSchema(createdDb);
            migratedSchema.Should().Contain(x => x.StartsWith("column "), $"{initializerName} DB must have tables");
            // A migration adding a NOT NULL column gives it a default to fill the existing rows,
            // which the model doesn't have - only a default the model declares must match
            var createdDefaults = createdSchema
                .Where(x => x.StartsWith(DefaultPrefix))
                .Select(GetDefaultColumn)
                .ToHashSet();
            var migratedOnly = migratedSchema.Except(createdSchema)
                .Where(x => !x.StartsWith(DefaultPrefix) || createdDefaults.Contains(GetDefaultColumn(x)));
            diffs.AddRange(migratedOnly.Select(x => $"{initializerName}, migrated only: {x}"));
            diffs.AddRange(createdSchema.Except(migratedSchema).Select(x => $"{initializerName}, created only: {x}"));
        }
        foreach (var diff in diffs)
            Out.WriteLine(diff);
        diffs.Should().BeEmpty("applying every migration must produce the schema EnsureCreated builds from the model");
    }

    // Private methods

    private static Dictionary<string, string> GetConnectionStrings(TestAppHost appHost)
        => appHost.Services.GetServices<IDbInitializer>().ToDictionary(
            x => x.GetType().Name,
            x => {
                // CreateDbContext lives on the generic DbInitializer<TDbContext>
                var createDbContext = x.GetType().GetMethod("CreateDbContext")!;
                using var dbContext = (DbContext)createDbContext.Invoke(x, [false])!;
                return dbContext.Database.GetConnectionString()!;
            });

    private static async Task DropDb(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        var dbName = builder.Database;
        builder.Database = "postgres";
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"drop database if exists \"{dbName}\" with (force)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<HashSet<string>> GetSchema(string connectionString)
    {
        string[] queries = [
            "select 'extension ' || extname from pg_extension",
            $"""
            select 'column ' || table_schema || '.' || table_name || '.' || column_name
                || ': ' || udt_name || coalesce('(' || character_maximum_length || ')', '')
                || coalesce(' precision=' || numeric_precision || ',' || numeric_scale, '')
                || coalesce(' datetime_precision=' || datetime_precision, '')
                || ' nullable=' || is_nullable
                || ' identity=' || is_identity || coalesce(' ' || identity_generation, '')
                || ' generated=' || is_generated || coalesce(' ' || generation_expression, '')
                || ' collation=' || coalesce(collation_name, '-')
            from information_schema.columns
            where table_schema {UserSchemaFilter} and table_name {LiveTableFilter}
            """,
            $"""
            select 'sequence ' || schemaname || '.' || sequencename || ': ' || data_type
                || ' start=' || start_value || ' increment=' || increment_by
                || ' min=' || min_value || ' max=' || max_value || ' cycle=' || cycle
            from pg_sequences
            where schemaname {UserSchemaFilter} and sequencename {LiveTableFilter}
            """,
            $"""
            select '{DefaultPrefix}' || table_schema || '.' || table_name || '.' || column_name
                || ' = ' || column_default
            from information_schema.columns
            where table_schema {UserSchemaFilter} and table_name {LiveTableFilter}
                and column_default is not null
            """,
            $"""
            select 'index ' || indexdef
            from pg_indexes
            where schemaname {UserSchemaFilter} and tablename {LiveTableFilter}
            """,
            $"""
            select 'constraint ' || r.relname || '.' || c.conname || ': ' || pg_get_constraintdef(c.oid)
            from pg_constraint c
                join pg_namespace n on n.oid = c.connamespace
                join pg_class r on r.oid = c.conrelid
            where n.nspname {UserSchemaFilter} and r.relname {LiveTableFilter}
            """,
            $"""
            select 'trigger ' || event_object_table || '.' || trigger_name || ': ' || action_statement
            from information_schema.triggers
            where trigger_schema {UserSchemaFilter} and event_object_table {LiveTableFilter}
            """,
        ];
        var result = new HashSet<string>();
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        foreach (var query in queries) {
            await using var command = new NpgsqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(reader.GetString(0));
        }
        return result;
    }

    private static string? GetDbName(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString).Database;

    private static string GetDefaultColumn(string defaultLine)
        => defaultLine[..defaultLine.IndexOf(" = ")];
}
