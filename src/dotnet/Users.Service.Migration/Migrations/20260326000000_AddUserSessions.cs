using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add name and expires_at columns to _Sessions table
            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "_Sessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "expires_at",
                table: "_Sessions",
                type: "timestamp with time zone",
                nullable: true);

            // Create user_sessions table
            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    session_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    is_api_key = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_sessions", x => new { x.user_id, x.session_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id",
                table: "user_sessions",
                column: "user_id");

            // Backfill user_sessions from existing _Sessions data
            migrationBuilder.Sql("""
                INSERT INTO user_sessions (user_id, session_id, is_api_key)
                SELECT user_id, id, false FROM "_Sessions"
                WHERE user_id IS NOT NULL AND user_id != '' AND NOT is_sign_out_forced
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_sessions");

            migrationBuilder.DropColumn(
                name: "name",
                table: "_Sessions");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "_Sessions");
        }
    }
}
