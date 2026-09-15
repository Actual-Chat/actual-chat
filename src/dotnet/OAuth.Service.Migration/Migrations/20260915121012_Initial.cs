using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.OAuth.Migrations;

/// <inheritdoc />
public partial class _20260915121012_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "_events",
            columns: table => new
            {
                uuid = table.Column<string>(type: "text", nullable: false, collation: "C"),
                version = table.Column<long>(type: "bigint", nullable: false),
                state = table.Column<int>(type: "integer", nullable: false),
                logged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                delay_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                value_json = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_events", x => x.uuid);
            });

        migrationBuilder.CreateTable(
            name: "_operations",
            columns: table => new
            {
                index = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                uuid = table.Column<string>(type: "text", nullable: false, collation: "C"),
                host_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                logged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                command_json = table.Column<string>(type: "text", nullable: false),
                items_json = table.Column<string>(type: "text", nullable: true),
                nested_operations = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_operations", x => x.index);
            });

        migrationBuilder.CreateTable(
            name: "open_iddict_applications",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                application_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                client_secret = table.Column<string>(type: "text", nullable: true),
                client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                consent_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                display_name = table.Column<string>(type: "text", nullable: true),
                display_names = table.Column<string>(type: "text", nullable: true),
                json_web_key_set = table.Column<string>(type: "text", nullable: true),
                permissions = table.Column<string>(type: "text", nullable: true),
                post_logout_redirect_uris = table.Column<string>(type: "text", nullable: true),
                properties = table.Column<string>(type: "text", nullable: true),
                redirect_uris = table.Column<string>(type: "text", nullable: true),
                requirements = table.Column<string>(type: "text", nullable: true),
                settings = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_open_iddict_applications", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "open_iddict_scopes",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                description = table.Column<string>(type: "text", nullable: true),
                descriptions = table.Column<string>(type: "text", nullable: true),
                display_name = table.Column<string>(type: "text", nullable: true),
                display_names = table.Column<string>(type: "text", nullable: true),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                properties = table.Column<string>(type: "text", nullable: true),
                resources = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_open_iddict_scopes", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "open_iddict_authorizations",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                application_id = table.Column<string>(type: "text", nullable: true),
                concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                properties = table.Column<string>(type: "text", nullable: true),
                scopes = table.Column<string>(type: "text", nullable: true),
                status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_open_iddict_authorizations", x => x.id);
                table.ForeignKey(
                    name: "fk_open_iddict_authorizations_open_iddict_applications_applica~",
                    column: x => x.application_id,
                    principalTable: "open_iddict_applications",
                    principalColumn: "id");
            });

        migrationBuilder.CreateTable(
            name: "open_iddict_tokens",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                application_id = table.Column<string>(type: "text", nullable: true),
                authorization_id = table.Column<string>(type: "text", nullable: true),
                concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                expiration_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                payload = table.Column<string>(type: "text", nullable: true),
                properties = table.Column<string>(type: "text", nullable: true),
                redemption_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_open_iddict_tokens", x => x.id);
                table.ForeignKey(
                    name: "fk_open_iddict_tokens_open_iddict_applications_application_id",
                    column: x => x.application_id,
                    principalTable: "open_iddict_applications",
                    principalColumn: "id");
                table.ForeignKey(
                    name: "fk_open_iddict_tokens_open_iddict_authorizations_authorization~",
                    column: x => x.authorization_id,
                    principalTable: "open_iddict_authorizations",
                    principalColumn: "id");
            });

        migrationBuilder.CreateIndex(
            name: "ix_events_delay_until_state_non_new",
            table: "_events",
            columns: new[] { "delay_until", "state" },
            filter: "state != 0");

        migrationBuilder.CreateIndex(
            name: "ix_events_pending",
            table: "_events",
            column: "delay_until",
            filter: "state = 0");

        migrationBuilder.CreateIndex(
            name: "ix_operations_logged_at",
            table: "_operations",
            column: "logged_at");

        migrationBuilder.CreateIndex(
            name: "ix_operations_uuid",
            table: "_operations",
            column: "uuid",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_open_iddict_applications_client_id",
            table: "open_iddict_applications",
            column: "client_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_open_iddict_authorizations_application_id_status_subject_ty~",
            table: "open_iddict_authorizations",
            columns: new[] { "application_id", "status", "subject", "type" });

        migrationBuilder.CreateIndex(
            name: "ix_open_iddict_scopes_name",
            table: "open_iddict_scopes",
            column: "name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_open_iddict_tokens_application_id_status_subject_type",
            table: "open_iddict_tokens",
            columns: new[] { "application_id", "status", "subject", "type" });

        migrationBuilder.CreateIndex(
            name: "ix_open_iddict_tokens_authorization_id",
            table: "open_iddict_tokens",
            column: "authorization_id");

        migrationBuilder.CreateIndex(
            name: "ix_open_iddict_tokens_reference_id",
            table: "open_iddict_tokens",
            column: "reference_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "_events");

        migrationBuilder.DropTable(
            name: "_operations");

        migrationBuilder.DropTable(
            name: "open_iddict_scopes");

        migrationBuilder.DropTable(
            name: "open_iddict_tokens");

        migrationBuilder.DropTable(
            name: "open_iddict_authorizations");

        migrationBuilder.DropTable(
            name: "open_iddict_applications");
    }
}
