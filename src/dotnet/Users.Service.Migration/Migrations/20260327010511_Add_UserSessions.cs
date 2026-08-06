using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class Add_UserSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Rename user_agent -> description
            migrationBuilder.RenameColumn(
                name: "user_agent",
                table: "_sessions",
                newName: "description");

            // 2. Add expires_at as nullable first (for backfill)
            migrationBuilder.AddColumn<DateTime>(
                name: "expires_at",
                table: "_sessions",
                type: "timestamp with time zone",
                nullable: true);

            // 3. Backfill expires_at from is_sign_out_forced (before dropping it)
            migrationBuilder.Sql("""
                UPDATE "_sessions" SET expires_at = last_seen_at
                WHERE is_sign_out_forced = true
                """);
            migrationBuilder.Sql("""
                UPDATE "_sessions" SET expires_at = now() + interval '3 months'
                WHERE expires_at IS NULL
                """);

            // 4. Make expires_at non-nullable now that all rows have a value
            migrationBuilder.AlterColumn<DateTime>(
                name: "expires_at",
                table: "_sessions",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            // 5. Drop old composite indexes that include is_sign_out_forced
            migrationBuilder.DropIndex(
                name: "ix_sessions_created_at_is_sign_out_forced",
                table: "_sessions");

            migrationBuilder.DropIndex(
                name: "ix_sessions_ipaddress_is_sign_out_forced",
                table: "_sessions");

            migrationBuilder.DropIndex(
                name: "ix_sessions_last_seen_at_is_sign_out_forced",
                table: "_sessions");

            migrationBuilder.DropIndex(
                name: "ix_sessions_user_id_is_sign_out_forced",
                table: "_sessions");

            // 6. Drop is_sign_out_forced column
            migrationBuilder.DropColumn(
                name: "is_sign_out_forced",
                table: "_sessions");

            // 7. Create new indexes on _sessions
            migrationBuilder.CreateIndex(
                name: "ix_sessions_created_at",
                table: "_sessions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_expires_at",
                table: "_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_ipaddress",
                table: "_sessions",
                column: "ipaddress");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_last_seen_at",
                table: "_sessions",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id",
                table: "_sessions",
                column: "user_id");

            // 8. Create user_sessions table
            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    session_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sessions", x => new { x.user_id, x.session_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_session_id",
                table: "user_sessions",
                column: "session_id");

            // 9. Backfill user_sessions from active sessions
            migrationBuilder.Sql("""
                INSERT INTO user_sessions (user_id, session_id)
                SELECT user_id, id FROM "_sessions"
                WHERE user_id IS NOT NULL AND user_id != '' AND expires_at > now()
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_sessions");

            migrationBuilder.DropIndex(
                name: "ix_sessions_created_at",
                table: "_sessions");

            migrationBuilder.DropIndex(
                name: "ix_sessions_expires_at",
                table: "_sessions");

            migrationBuilder.DropIndex(
                name: "ix_sessions_ipaddress",
                table: "_sessions");

            migrationBuilder.DropIndex(
                name: "ix_sessions_last_seen_at",
                table: "_sessions");

            migrationBuilder.DropIndex(
                name: "ix_sessions_user_id",
                table: "_sessions");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "_sessions");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "_sessions",
                newName: "user_agent");

            migrationBuilder.AddColumn<bool>(
                name: "is_sign_out_forced",
                table: "_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_created_at_is_sign_out_forced",
                table: "_sessions",
                columns: new[] { "created_at", "is_sign_out_forced" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_ipaddress_is_sign_out_forced",
                table: "_sessions",
                columns: new[] { "ipaddress", "is_sign_out_forced" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_last_seen_at_is_sign_out_forced",
                table: "_sessions",
                columns: new[] { "last_seen_at", "is_sign_out_forced" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id_is_sign_out_forced",
                table: "_sessions",
                columns: new[] { "user_id", "is_sign_out_forced" });
        }
    }
}
